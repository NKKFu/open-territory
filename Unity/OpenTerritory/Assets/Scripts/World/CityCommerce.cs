using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace OpenTerritory
{
    public sealed class CityCommerce : MonoBehaviour
    {
        [Serializable] sealed class SaveData { public int version = 1, balance = 320, shipments; public string loadout, wardrobe; }
        sealed class Merchandise
        {
            public string id, name; public int price, buyback, pack, ammo;
            public Merchandise(string i, string n, int p, int b, int size, int cost) { id = i; name = n; price = p; buyback = b; pack = size; ammo = cost; }
        }
        static readonly Merchandise[] Goods = {
            new Merchandise("pulse", "Pulsor", 0, 0, 4, 16),
            new Merchandise("sidearm", "P-9", 180, 105, 12, 24),
            new Merchandise("carbine", "C-12", 360, 205, 20, 40)
        };
        static readonly int[] Orders = { 2, 3, 1, 4 };
        CityContext ctx;
        readonly Dictionary<string, Transform> clerks = new Dictionary<string, Transform>();
        readonly Dictionary<string, PlaceData> places = new Dictionary<string, PlaceData>();
        readonly List<Transform> fishModels = new List<Transform>(), weightModels = new List<Transform>();
        Transform fishWorker, beam, leftPan, rightPan;
        Vector3 beamOrigin;
        int balance = 320, shipments, fishCount = 2, weightCount, fishLine;
        float nextCheckpoint, scaleAngle;
        string savePath, lastSaved;
        bool qaRun;
        public int Balance { get { return balance; } }
        public int Shipments { get { return shipments; } }
        public int NextWeightUnits { get { return Orders[shipments % Orders.Length]; } }
        public int FishUnits { get { return fishCount; } }
        public int WeightUnits { get { return weightCount; } }
        public string SavePath { get { return savePath; } }
        public static string ReviewFlag
        {
            get { return Array.Find(Environment.GetCommandLineArgs(), arg => arg == "--city-debug" || arg == "--city-qa" || arg.StartsWith("--city-") && arg.EndsWith("-qa")); }
        }
        public static bool IsReviewRun { get { return ReviewFlag != null; } }

        public void Initialize(CityContext context)
        {
            ctx = context;
            foreach (var place in ctx.Data.places) places[place.name] = place;
            foreach (var p in ctx.Data.residents)
                if (!string.IsNullOrEmpty(p.place) && !string.IsNullOrEmpty(p.role)) { var t = ctx.Node(p.id); if (t) clerks[p.place] = t; }
            qaRun = IsReviewRun;
            string storage = Application.persistentDataPath;
            if (qaRun) storage = Path.Combine(Application.temporaryCachePath, "BaiaDoSol-QA", ReviewFlag.Substring(2) + "-" + System.Diagnostics.Process.GetCurrentProcess().Id);
            savePath = Path.Combine(storage, "baia-wallet-v1.json");
            if (!qaRun) Restore();
            foreach (var source in ctx.Data.services)
            {
                var service = source;
                if (service.kind != "commerce" && service.kind != "bank" && service.kind != "clinic" && service.kind != "range-card") continue;
                ctx.AddAction("commerce-" + service.id, () => Point(service), () => Label(service), () => Use(service),
                    () => !ctx.Remote.Active && !ctx.Combat.Down, 2.65f, target: () => ctx.Node(service.node));
            }
            var data = ctx.Data.fish; fishWorker = ctx.Node(data.worker); beam = ctx.Node(data.beam); leftPan = ctx.Node(data.leftPan); rightPan = ctx.Node(data.rightPan);
            beamOrigin = beam ? beam.position : data.position + Vector3.up * 2.3f;
            foreach (int id in data.fish) { var t = ctx.Node(id); if (t) fishModels.Add(t); }
            foreach (int id in data.weights) { var t = ctx.Node(id); if (t) weightModels.Add(t); }
            for (int i = 0; i < data.actions.Length; i++)
            {
                int index = i;
                ctx.AddAction("fish-" + index, () => data.actions[index] + Vector3.up * .95f,
                    () => index == 0 ? "Conversar" : index == 1 ? "Escolher" : index == 2 ? "Trocar pesos" : "Entregar " + Weight(NextWeightUnits),
                    () => FishAction(index), () => !ctx.Remote.Active && !ctx.Combat.Down && (index == 1 || index == 2 || FishReady()), index == 0 ? 2.8f : 2.35f,
                    priority: index == 0 ? 2 : 0, conversation: index == 0, target: () => index == 0 ? fishWorker : null,
                    focus: () => index == 0 && fishWorker ? fishWorker.position + Vector3.up * 1.4f : data.actions[index] + Vector3.up * .95f);
            }
            UpdateScale(1); Checkpoint();
        }
        Vector3 Point(ServiceData point) { return point.anchor == Vector3.zero ? point.position + Vector3.up : point.anchor; }
        bool ClerkReady(ServiceData point)
        {
            if (ctx.Combat.IsThreatening() || ctx.Police.TradeBlocked || ctx.Combat.Down) return false;
            if (string.IsNullOrEmpty(point.place) || !clerks.TryGetValue(point.place, out Transform clerk) || !clerk || !ctx.Residents.CanTalk(clerk)) return false;
            if (!places.TryGetValue(point.place, out var building)) return false;
            return building.Contains(clerk.position);
        }
        bool FishReady() { return fishWorker && ctx.Residents.CanTalk(fishWorker) && !ctx.Combat.IsThreatening() && !ctx.Police.TradeBlocked; }
        static Merchandise Item(string id) { foreach (var item in Goods) if (item.id == id) return item; return null; }
        public static string Cash(int amount) { return "R$ " + amount.ToString(CultureInfo.InvariantCulture); }
        public static string Weight(int units) { return (units * .5f).ToString("0.#", CultureInfo.GetCultureInfo("en-US")) + " kg"; }
        string Label(ServiceData s)
        {
            if (s.kind == "bank" || s.mode == "ledger") return "Extrato";
            if (s.kind == "range-card") return "Resultados";
            if (!ClerkReady(s)) return "Chamar atendente";
            if (s.kind == "clinic") return "Atendimento";
            return Quote(s.mode, s.item);
        }
        public string Quote(string mode, string id)
        {
            var sku = Item(id); if (sku == null) return "Consultar equipamento";
            bool owned = ctx.Combat.Owns(id);
            if (mode == "sell") return (owned ? "Vender " : "Avaliar ") + sku.name + (owned ? " · " + Cash(sku.buyback) : "");
            if (!owned && sku.price > 0) return "Comprar " + sku.name + " · " + Cash(sku.price);
            int amount = Mathf.Clamp(ctx.Combat.MaxReserve(id) - ctx.Combat.Reserve(id), 0, sku.pack);
            return amount > 0 ? "Repor " + sku.name + " · " + Cash(Mathf.CeilToInt(sku.ammo * amount / (float)sku.pack)) : "Consultar " + sku.name;
        }
        void Use(ServiceData service)
        {
            Vector3 at = Point(service) + Vector3.up * .75f;
            if (service.kind == "bank" || service.mode == "ledger")
            {
                var owned = new List<string>(); foreach (var item in Goods) if (ctx.Combat.Owns(item.id)) owned.Add(item.name);
                ctx.Say("Saldo: " + Cash(balance) + "\nKit: " + string.Join(", ", owned), at, 6); return;
            }
            if (service.kind == "range-card")
            {
                int rounds = ctx.Combat.PracticeRounds, hits = ctx.Combat.PracticeHits;
                ctx.Say(rounds > 0 ? hits + " acertos · " + rounds + " disparos" :
                    "Use as baias amarelas.", at, 7); return;
            }
            if (!ClerkReady(service)) { ctx.Say(ctx.Combat.IsThreatening() ? "Guarde a arma." : "Agora não.", at, 5); return; }
            if (service.kind == "clinic")
            {
                float restored = ctx.Combat.HealAtClinic();
                ctx.Say(restored > 0 ? "Pronto. Sem custo." : "Está tudo bem.", at, 6); return;
            }
            bool success = Trade(service.mode, service.item, out string receipt);
            ctx.Say(receipt, at, 4); if (success) ctx.Sound("register", service.position, .55f);
        }
        public bool Trade(string mode, string id, out string receipt)
        {
            var sku = Item(id);
            if (sku == null) { receipt = "Indisponível."; return false; }
            if (mode == "sell")
            {
                if (sku.buyback <= 0) { receipt = "Sem recompra."; return false; }
                if (!ctx.Combat.Remove(id)) { receipt = "Você não tem " + sku.name + "."; return false; }
                balance = Mathf.Min(999999, balance + sku.buyback); Checkpoint();
                receipt = sku.name + " · +" + Cash(sku.buyback) + "\nSaldo: " + Cash(balance); return true;
            }
            if (!ctx.Combat.Owns(id))
            {
                if (sku.price <= 0) { receipt = "Já está no kit."; return false; }
                if (balance < sku.price) { receipt = Insufficient(sku.price); return false; }
                if (!ctx.Combat.Grant(id)) { receipt = "Você já tem."; return false; }
                balance -= sku.price; Checkpoint(); receipt = sku.name + " · -" + Cash(sku.price) + "\nSaldo: " + Cash(balance); return true;
            }
            int amount = Mathf.Clamp(ctx.Combat.MaxReserve(id) - ctx.Combat.Reserve(id), 0, sku.pack);
            if (amount == 0) { receipt = "Reserva cheia."; return false; }
            int cost = Mathf.CeilToInt(sku.ammo * amount / (float)sku.pack);
            if (balance < cost) { receipt = Insufficient(cost); return false; }
            int added = ctx.Combat.AddAmmo(id, amount);
            if (added <= 0) { receipt = "Reserva cheia."; return false; }
            cost = Mathf.CeilToInt(sku.ammo * added / (float)sku.pack); balance -= cost; Checkpoint();
            receipt = "+" + added + (id == "pulse" ? " cargas" : " cartuchos") + " · -" + Cash(cost) + "\nSaldo: " + Cash(balance); return true;
        }
        string Insufficient(int price) { return "Faltam " + Cash(price - balance) + " · Saldo: " + Cash(balance); }
        void FishAction(int index)
        {
            var point = ctx.Data.fish.actions[index] + Vector3.up * 1.75f;
            if (index == 0)
            {
                string[] lines = {
                    "Sou o Nilo. Peixe fresco hoje.",
                    "Cada peça pesa meio quilo.",
                    "Peso certo, pagamento na hora.",
                    "O pedido está no recibo."
                };
                ctx.Say(lines[fishLine++ % lines.Length], point, 4); return;
            }
            if (index == 3)
            {
                if (!FishReady()) return;
                bool paid = Dispatch(out string receipt); ctx.Say(receipt, point, 4); if (paid) ctx.Sound("register", point, .6f); return;
            }
            if (index == 1) fishCount = fishCount % 4 + 1; else weightCount = (weightCount + 1) % 5;
            ctx.Say("Peixe: " + Weight(fishCount) + " · Pesos: " + Weight(weightCount), beamOrigin + Vector3.up * .9f, 3);
            ctx.Sound("plate", beamOrigin, .4f);
        }
        public bool Dispatch(out string receipt)
        {
            int target = NextWeightUnits;
            if (fishCount != target || weightCount != target)
            {
                receipt = "Este caixote pede " + Weight(target) + ". Escolha os peixes na caixa azul e iguale os pratos com os pesos da caixa de madeira."; return false;
            }
            int payout = 40 + target * 8; balance = Mathf.Min(999999, balance + payout); shipments++; Checkpoint();
            receipt = "Lote de " + Weight(target) + " recebido. +" + Cash(payout) + ". Saldo: " + Cash(balance) + ". Próximo caixote: " + Weight(NextWeightUnits) + "."; return true;
        }
        public int Fine(int amount)
        {
            int paid = Mathf.Clamp(amount, 0, balance); balance -= paid; if (paid > 0) Checkpoint(); return paid;
        }
        public int ReceiveCash(int amount)
        {
            int received = Mathf.Clamp(amount, 0, 999999 - balance);
            balance += received; if (received > 0) Checkpoint(); return received;
        }
        public bool TryPurchase(int price, Action grant)
        {
            if (price < 0 || balance < price || grant == null || !ctx || ctx.Paused) return false;
            balance -= price; grant(); Checkpoint(); return true;
        }
        void Update()
        {
            if (!ctx || !ctx.Ready || ctx.Paused) return; UpdateScale(Time.deltaTime);
            if (ctx.Clock >= nextCheckpoint) { nextCheckpoint = ctx.Clock + 1; Checkpoint(); }
        }
        void UpdateScale(float dt)
        {
            float target = Mathf.Clamp((fishCount - weightCount) * .10f, -.35f, .35f);
            scaleAngle = Mathf.Lerp(scaleAngle, target, 1 - Mathf.Exp(-dt * 5));
            if (beam) beam.localRotation = Quaternion.Euler(0, 0, scaleAngle * Mathf.Rad2Deg);
            if (leftPan) leftPan.position = beamOrigin + new Vector3(-1.12f * Mathf.Cos(scaleAngle), -1.12f * Mathf.Sin(scaleAngle), 0);
            if (rightPan) rightPan.position = beamOrigin + new Vector3(1.12f * Mathf.Cos(scaleAngle), 1.12f * Mathf.Sin(scaleAngle), 0);
            for (int i = 0; i < fishModels.Count; i++) if (fishModels[i]) fishModels[i].gameObject.SetActive(i < fishCount);
            for (int i = 0; i < weightModels.Count; i++) if (weightModels[i]) weightModels[i].gameObject.SetActive(i < weightCount);
        }
        void Restore()
        {
            try
            {
                if (!File.Exists(savePath)) return;
                var saved = JsonUtility.FromJson<SaveData>(File.ReadAllText(savePath));
                if (saved == null || saved.version != 1 || saved.balance < 0 || saved.balance > 999999 || saved.shipments < 0 || string.IsNullOrEmpty(saved.loadout)) return;
                ctx.Combat.RestoreLoadoutJson(saved.loadout); if (ctx.Wardrobe) ctx.Wardrobe.RestoreJson(saved.wardrobe); balance = saved.balance; shipments = saved.shipments;
            }
            catch (Exception error) { Debug.LogWarning("O arquivo da carteira não pôde ser lido: " + error.Message); }
        }
        public void Checkpoint()
        {
            if (!ctx || !ctx.Combat || string.IsNullOrEmpty(savePath)) return;
            var saved = new SaveData { balance = balance, shipments = shipments, loadout = ctx.Combat.ExportLoadoutJson(), wardrobe = ctx.Wardrobe ? ctx.Wardrobe.ExportJson() : null };
            string json = JsonUtility.ToJson(saved); if (json == lastSaved) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(savePath)); string pending = savePath + ".pending";
                File.WriteAllText(pending, json, new UTF8Encoding(false));
                if (File.Exists(savePath)) File.Replace(pending, savePath, savePath + ".backup"); else File.Move(pending, savePath);
                lastSaved = json;
            }
            catch (Exception error) { Debug.LogWarning("A carteira não pôde ser salva: " + error.Message); }
        }
        void OnApplicationPause(bool paused) { if (paused) Checkpoint(); }
        void OnApplicationQuit() { Checkpoint(); }
    }
}
