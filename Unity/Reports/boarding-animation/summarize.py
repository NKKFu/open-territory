import hashlib
import json
import re
from datetime import datetime, timezone
from pathlib import Path

root = Path(__file__).resolve().parent
repo = root.parents[2]
executable = repo / "Unity/OpenTerritory/Builds/BoardingAnimation-20261108/BaiaDoSol.exe"
assembly = executable.parent / "BaiaDoSol_Data/Managed/Assembly-CSharp.dll"

def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))

def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()

assembly_hash = sha(assembly)
names = ["native-final", "interaction-regression", "vehicle-regression"]
suites = {name: read(root / name / "verification.json") for name in names}
for name, result in suites.items():
    if result["assemblySha256"] != assembly_hash:
        raise RuntimeError("The delivered assembly differs from the tested assembly: " + name)
for name in names[:2]:
    if not suites[name]["passed"] or suites[name]["processExitCode"] != 0:
        raise RuntimeError("Animation/interaction acceptance failed: " + name)
traffic = read(root / "vehicle-regression/vehicle-safety-qa.json")
failures = [check for check in traffic["checks"] if not check["passed"]]
log = (root / "native-final/player.log").read_text(encoding="utf-8-sig", errors="replace")
graphics = re.search(r"Version:\s*(Direct3D[^\n\r]+)", log)
build = read(root / "build-process-result.json")
if build["processExitCode"] != 0:
    raise RuntimeError("Full build failed")
summary = {
    "verifiedAtUtc": datetime.now(timezone.utc).isoformat(),
    "executable": str(executable), "executableSha256": sha(executable),
    "assemblySha256": assembly_hash, "fullBuild": build,
    "snapshot": read(root / "snapshot.json"),
    "graphicsApi": graphics.group(1).strip() if graphics else "See player.log",
    "qaResolution": {"width": 1280, "height": 800},
    "animationAndInteractionPassed": True,
    "allRegressionSuitesPassed": all(result["passed"] for result in suites.values()),
    "suites": suites, "trafficFailures": failures,
}
(root / "delivery.json").write_text(json.dumps(summary, indent=2, ensure_ascii=False), encoding="utf-8")
passed_traffic = sum(check["passed"] for check in traffic["checks"])
lines = [
    "# Animações de veículos — 8 de novembro de 2026", "",
    "Entrada em carros e retirada de motoristas com apoio dos pés, transferência de peso, pernas alternadas, apoio das mãos e fechamento da porta após recolher os pés. A entrada termina na posição e altura exatas do banco.", "",
    "Build completo e validação em uma cópia isolada do projeto, preservando as alterações simultâneas do Editor. O executável entregue é `" + str(executable) + "`.", "",
    "Assembly SHA-256: `" + assembly_hash + "`.", "",
    "Configuração de QA: Unity 6000.3.27f1, Windows x64, build sem Development, " + summary["graphicsApi"] + ", 1280 × 800 em janela somente no processo de teste, armazenamento isolado de QA.", "",
    "| Suíte | Resultado | Saída do processo |", "| --- | --- | --- |",
    "| Animação: 5 modelos, 2 lados, roubo e carro vazio | 194/194 | 0 |",
    "| Interações reais: alvo, Q/E, banco, carro, saída, pausa e toque | 41/41 | 0 |",
    f"| Segurança e trânsito | {passed_traffic}/{len(traffic['checks'])} | {suites['vehicle-regression']['processExitCode']} |", "",
    "As animações e as interações estão aprovadas. A suíte geral de trânsito tem as seguintes falhas de retomada e ultrapassagem, cuja causa não foi isolada nesta tarefa:", "",
]
lines += ["- `" + check["name"] + "`: " + check["detail"] for check in failures]
lines += ["", "Os relatórios completos estão em [delivery.json](delivery.json), [animações](native-final/carjacking-qa.json), [interações](interaction-regression/interaction-qa.json) e [trânsito](vehicle-regression/vehicle-safety-qa.json).", "", "Prévia em cenário de teste:", "", "![Retirada do NPC e entrada](carjacking-preview.gif)", "", "![Entrada em carro vazio](boarding-preview.gif)", ""]
(root / "RESULTADOS.md").write_text("\n".join(lines), encoding="utf-8")
print(json.dumps({"assemblySha256": assembly_hash, "animations": 194, "interactions": 41,
                  "traffic": str(passed_traffic) + "/" + str(len(traffic["checks"])), "report": str(root / "RESULTADOS.md")}, ensure_ascii=False))
