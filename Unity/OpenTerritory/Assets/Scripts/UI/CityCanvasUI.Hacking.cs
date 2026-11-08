using UnityEngine;
using UnityEngine.UI;

namespace OpenTerritory
{
    public sealed partial class CityCanvasUI
    {
        RectTransform hackCard, hackSearch, remoteDriveCard;
        Text hackDeviceText, hackActionText, hackStatusText, hackStepText, remoteDriveText;
        Button hackExecute, hackCycle;
        Image hackAccent, remoteSignal;
        readonly Image[] hackSteps = new Image[8];
        readonly Image[] searchPulse = new Image[3];
        readonly Color hackGreen = new Color(.48f, .94f, .72f), hackPanel = new Color(.025f, .065f, .082f, .97f), hackAmber = new Color(1, .75f, .39f);
        public bool HackingCardVisible => hackCard && hackCard.gameObject.activeSelf;
        public bool RemoteDriveCardVisible => remoteDriveCard && remoteDriveCard.gameObject.activeSelf;
        public Button HackExecuteButton => hackExecute;
        public Button HackCycleButton => hackCycle;

        void HackBox(RectTransform rect, float x, float y, float width, float height)
        { rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); }
        void HackStyle(Button button, Color normal)
        {
            button.GetComponent<Image>().color = normal;
            if (((RectTransform)button.transform).rect.width <= 28)
            {
                var caption = button.GetComponentInChildren<Text>(); caption.fontSize = 12;
                Stretch(caption.rectTransform, 2, 2, 1, 1);
            }
            var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(.77f, 1, .89f);
            colors.pressedColor = new Color(.56f, .82f, .70f); colors.selectedColor = Color.white; colors.disabledColor = new Color(.55f, .62f, .62f, .7f);
            colors.fadeDuration = .09f; button.colors = colors;
            var navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
        }
        RectTransform HackPanel(string name, float width, float height)
        {
            var card = Rect(name, root); card.pivot = new Vector2(.5f, 0); card.sizeDelta = new Vector2(width, height);
            Paint(card, hackPanel);
            var border = card.gameObject.AddComponent<Outline>(); border.effectColor = new Color(.3f, .59f, .52f, .45f); border.effectDistance = new Vector2(1, -1);
            var tail = Rect("Ligação ao alvo", card); tail.anchorMin = tail.anchorMax = new Vector2(.5f, 0); tail.pivot = new Vector2(.5f, 1); tail.sizeDelta = new Vector2(2, 18); Paint(tail, hackGreen);
            return card;
        }
        void BuildHackingUI()
        {
            hackCard = HackPanel("Conexão hacker · painel contextual", 244, 92);
            var rail = Rect("Estado da conexão", hackCard); HackBox(rail, 0, 0, 2, 92); hackAccent = Paint(rail, hackGreen);
            hackDeviceText = Label("Dispositivo", hackCard, "", 12, ink); hackDeviceText.fontStyle = FontStyle.Bold; HackBox(hackDeviceText.rectTransform, 10, 5, 196, 18);
            var hide = Button("Ocultar modo hacker", hackCard, "Z", () => ctx.Input.TouchHackerToggle = true); HackBox((RectTransform)hide.transform, 216, 5, 20, 20); HackStyle(hide, new Color(.12f, .22f, .24f));
            hackExecute = Button("Executar hack", hackCard, "", () => ctx.Input.TouchHackPulse = true, out hackActionText);
            HackBox((RectTransform)hackExecute.transform, 8, 28, 228, 32); HackStyle(hackExecute, new Color(.12f, .28f, .25f));
            hackActionText.fontSize = 17; hackActionText.fontStyle = FontStyle.Bold; hackActionText.alignment = TextAnchor.MiddleLeft; Stretch(hackActionText.rectTransform, 40, 6, 2, 2);
            var key = Rect("Atalho F", hackExecute.transform); HackBox(key, 5, 5, 22, 22); Paint(key, hackGreen);
            var keyText = Label("Tecla", key, "F", 14, deep, TextAnchor.MiddleCenter); keyText.fontStyle = FontStyle.Bold; Stretch(keyText.rectTransform);
            hackCycle = Button("Próxima ação hacker", hackCard, "", () => ctx.Input.TouchHackNext = true, out hackStepText);
            HackBox((RectTransform)hackCycle.transform, 8, 66, 76, 20); HackStyle(hackCycle, new Color(.075f, .15f, .18f));
            hackStepText.fontSize = 11; hackStepText.alignment = TextAnchor.MiddleLeft; Stretch(hackStepText.rectTransform, 5, 2, 1, 1);
            hackStatusText = Label("Estado", hackCard, "", 11, hackGreen, TextAnchor.MiddleRight); HackBox(hackStatusText.rectTransform, 88, 66, 148, 20);
            for (int i = 0; i < hackSteps.Length; i++)
            { var pip = Rect("Ação " + (i + 1), hackCard); HackBox(pip, 10 + i * 28, 89, 22, 1); hackSteps[i] = Paint(pip, hackGreen); }

            hackSearch = HackPanel("Hacker · procurando dispositivos", 132, 28);
            var searchTitle = Label("Busca", hackSearch, "Buscando…", 12, ink); HackBox(searchTitle.rectTransform, 30, 4, 78, 20);
            var hideSearch = Button("Ocultar busca", hackSearch, "Z", () => ctx.Input.TouchHackerToggle = true); HackBox((RectTransform)hideSearch.transform, 108, 5, 18, 18); HackStyle(hideSearch, new Color(.12f, .22f, .24f));
            for (int i = 0; i < searchPulse.Length; i++)
            { var pulse = Rect("Pulso de busca " + i, hackSearch); HackBox(pulse, 10 + i * 5, 12 - i * 2, 2, 5 + i * 2); searchPulse[i] = Paint(pulse, hackGreen); }

            remoteDriveCard = HackPanel("Controle remoto · conexão do carro", 196, 38);
            var driveTitle = Label("Conexão remota", remoteDriveCard, "CARRO REMOTO", 11, hackGreen); HackBox(driveTitle.rectTransform, 10, 8, 105, 20);
            remoteDriveText = Label("Voltar ao personagem", remoteDriveCard, "Voltar", 11, ink); HackBox(remoteDriveText.rectTransform, 122, 8, 36, 20);
            var exit = Button("Desconectar carro", remoteDriveCard, "E", () => ctx.Input.TouchInteract = true); HackBox((RectTransform)exit.transform, 166, 8, 20, 20); HackStyle(exit, new Color(.12f, .28f, .25f));
            var signalTrack = Rect("Sinal · fundo", remoteDriveCard); HackBox(signalTrack, 8, 34, 180, 2); Paint(signalTrack, new Color(.13f, .23f, .25f));
            var signal = Rect("Sinal do controle remoto", signalTrack); HackBox(signal, 0, 0, 180, 2); remoteSignal = Paint(signal, hackGreen);
            hackCard.gameObject.SetActive(false); hackSearch.gameObject.SetActive(false); remoteDriveCard.gameObject.SetActive(false);
        }
        public void RefreshHackingUI()
        {
            if (!hackCard || !ctx || !ctx.Ready) return;
            var remote = ctx.Remote;
            bool visible = !ctx.Paused && remote.PreviewVisible;
            bool target = visible && remote.SelectedDevice != null && remote.ActionCount > 0;
            hackCard.gameObject.SetActive(target);
            hackSearch.gameObject.SetActive(visible && !target);
            remoteDriveCard.gameObject.SetActive(!ctx.Paused && remote.VehicleActive && !visible);
            if (target)
            {
                var option = remote.CurrentHackOption; float cooldown = remote.SelectedCooldown;
                bool ready = option.Available && cooldown <= 0;
                hackDeviceText.text = remote.SelectedDeviceName; hackActionText.text = option.Label;
                hackAccent.color = ready ? hackGreen : hackAmber;
                hackExecute.interactable = ready;
                hackStepText.text = remote.ActionCount > 1 ? "Tab  " + (remote.CurrentHackIndex + 1) + "/" + remote.ActionCount : "";
                hackCycle.interactable = remote.ActionCount > 1;
                float distance = Vector3.Distance(remote.HackOrigin, remote.SelectedDevice.position);
                string detail = cooldown > 0 ? Mathf.CeilToInt(cooldown) + " s" : !option.Available && string.IsNullOrEmpty(option.Effect) ? "Indisponível" : option.Effect;
                hackStatusText.text = (string.IsNullOrEmpty(detail) ? "" : detail + " · ") + Mathf.RoundToInt(distance) + " m";
                hackStatusText.color = ready ? hackGreen : hackAmber;
                for (int i = 0; i < hackSteps.Length; i++)
                { hackSteps[i].gameObject.SetActive(i < remote.ActionCount); hackSteps[i].color = i == remote.CurrentHackIndex ? hackAccent.color : new Color(.13f, .23f, .25f); }
                if (!Place(hackCard, remote.SelectedDevice.position + Vector3.up * .25f)) hackCard.gameObject.SetActive(false);
            }
            if (hackSearch.gameObject.activeSelf)
            {
                Place(hackSearch, ctx.Camera.transform.position + ctx.Camera.transform.forward * 4 - ctx.Camera.transform.up * 1.3f);
                for (int i = 0; i < searchPulse.Length; i++) searchPulse[i].color = Color.Lerp(new Color(.15f, .35f, .29f), hackGreen, .5f + .5f * Mathf.Sin(ctx.Clock * 5 - i));
            }
            if (remoteDriveCard.gameObject.activeSelf)
            {
                var car = ctx.Vehicles.RemoteVehicle; float distance = Vector3.Distance(ctx.Player.position, car.Position);
                remoteSignal.rectTransform.sizeDelta = new Vector2(180 * Mathf.Clamp01(1 - distance / 120), 2);
                remoteSignal.color = distance < 95 ? hackGreen : hackAmber;
                Place(remoteDriveCard, ctx.Vehicles.HackPoint(car.Root));
            }
        }
    }
}
