# Passadas compartilhadas e corrida a 9,2 m/s

Verificado em 2026-11-07T21:57:45.6476941Z.

O protagonista corre a 9,2 m/s (antes 7,4 m/s, aumento de 24,3%). O ciclo de corrida chega a 4,3 m e combina apoio curto, recuperação da perna e cadência proporcionais ao deslocamento real. A câmera usa a mesma referência de velocidade.

Moradores e motoristas com rig humanoide compartilham o apoio fixo dos pés, flexão das pernas, braços opostos e transferência de peso. A passada considera o comprimento das pernas e a escala do modelo; NPCs mais lentos usam passos menores. Cada identidade mantém uma fase própria. Os sons seguem o contato visível. O apoio termina antecipadamente quando o alcance da perna se esgota numa curva. O pouso usa a última posição visível do tornozelo, já corrigida pelo alcance.

## Validação nativa

- 121 verificações de animação/NPCs aprovadas em [gait-qa.json](gait/gait-qa.json).
- 24 verificações de controles, câmera e mira aprovadas em [qa.json](controls/qa.json).
- Nenhum erro de execução no probe de passadas.
- Executável: `C:\Users\nelso\open-territory\Unity\OpenTerritory\Builds\WindowsRelease\BaiaDoSol.exe`.
- Assembly SHA-256: `5B037EC947231CD4EF305AA632D8B7F9881FC75653D3D0973E82286615342FFF`.
- As medições finais usam o player Windows com `-nographics`, sem mudar o relógio ou a configuração da partida. Capturas gráficas foram inspecionadas separadamente; testes gráficos simultâneos apresentaram contenção e não servem como medição de cadência.

| Movimento | NPC | Escala | Velocidade real (m/s) | Contatos/s | Maior velocidade vertical do quadril (m/s) |
|---|---:|---:|---:|---:|---:|
| walk | - | 1.00 | 2.65 | 3.22 | 2.06 |
| sprint | - | 1.00 | 9.20 | 4.18 | 0.67 |
| sprint-turn | - | 1.00 | 8.76 | 4.52 | 1.29 |
| aim-strafe | - | 1.00 | 1.75 | 2.25 | 0.37 |
| slope-walk | - | 1.00 | 2.65 | 3.21 | 1.86 |
| npc-small-slow-walk | 17416 | 0.86 | 0.65 | 1.61 | 0.21 |
| npc-small-walk | 17416 | 0.86 | 1.15 | 2.25 | 0.75 |
| npc-small-jog | 17416 | 0.86 | 2.80 | 2.90 | 1.22 |
| npc-run-turn | 17416 | 0.86 | 3.10 | 2.90 | 1.49 |
| npc-tall-walk | 4683 | 1.08 | 1.15 | 1.93 | 0.26 |
| npc-tall-jog | 4683 | 1.08 | 2.80 | 2.57 | 0.63 |

São modelos reais de moradores móveis nas escalas 0,86 e 1,08, movimentados por seus próprios CharacterControllers no piso isolado do probe. A animação permanece na atualização normal de CityResidents. O QA dirige esses controladores diretamente para isolar as passadas da navegação. Também cobre dois modelos sentados, caminhada lenta, fuga a 2,8 m/s, curva a 3,1 m/s, parada e pausa. Não é uma auditoria da escolha de rotas nem do restante da simulação.

Reprodução: `powershell -NoProfile -ExecutionPolicy Bypass -File Unity/Verificar-Passadas.ps1 -NoGraphics`. Sem `-NoGraphics`, o probe também grava as oito fases dos ciclos; execute a sessão gráfica sem outros players de teste disputando a GPU.
