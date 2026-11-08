# Animações de veículos — 8 de novembro de 2026

Entrada em carros e retirada de motoristas com apoio dos pés, transferência de peso, pernas alternadas, apoio das mãos e fechamento da porta após recolher os pés. A entrada termina na posição e altura exatas do banco.

Build completo e validação em uma cópia isolada do projeto, preservando as alterações simultâneas do Editor. O executável entregue é `C:\Users\nelso\open-territory\Unity\OpenTerritory\Builds\BoardingAnimation-20261108\BaiaDoSol.exe`.

Assembly SHA-256: `E8130C5A10045C270E262202962A20A2190D5D3190C52BD83C17ED6B5800BE62`.

Configuração de QA: Unity 6000.3.27f1, Windows x64, build sem Development, Direct3D 12 [level 12.2], 1280 × 800 em janela somente no processo de teste, armazenamento isolado de QA.

| Suíte | Resultado | Saída do processo |
| --- | --- | --- |
| Animação: 5 modelos, 2 lados, roubo e carro vazio | 194/194 | 0 |
| Interações reais: alvo, Q/E, banco, carro, saída, pausa e toque | 41/41 | 0 |
| Segurança e trânsito | 52/56 | 1 |

As animações e as interações estão aprovadas. A suíte geral de trânsito tem as seguintes falhas de retomada e ultrapassagem, cuja causa não foi isolada nesta tarefa:

- `player-centre-resumes-after-clear`: 0.26
- `wet-road-resumes-after-clear`: 0.26
- `parked-car-is-passed-and-own-lane-recovered`: side=0.4382629 phase=out denied=obstaculo-Group-8908 progress=-7.69728
- `passing-signals-before-steering`: signal lead=-117.8927

Os relatórios completos estão em [delivery.json](delivery.json), [animações](native-final/carjacking-qa.json), [interações](interaction-regression/interaction-qa.json) e [trânsito](vehicle-regression/vehicle-safety-qa.json).

Prévia em cenário de teste:

![Retirada do NPC e entrada](carjacking-preview.gif)

![Entrada em carro vazio](boarding-preview.gif)
