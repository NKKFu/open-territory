Correção dos volantes — 8 de novembro de 2026

O encaixe original deixava o topo do aro atravessar o para-brisa inclinado e a parte inferior entrar no painel. Os 34 carros agora usam o volante a 0,50 m à frente do assento, com inclinação de 18°. As mãos acompanham o plano inclinado durante o esterço. O ajuste é aplicado na criação do veículo e na importação da cena; o comando do Editor atualiza somente os transforms dos volantes e guarda uma cópia da cena anterior.

O executável entregue é `Unity/OpenTerritory/Builds/SteeringWheels-20261108-B/BaiaDoSol.exe`. Foi produzido com um build completo, `BuildOptions.None`, em uma cópia temporária congelada do projeto, usando Unity 6000.3.27f1. A compilação terminou com zero erros e código de saída 0. A cópia evita que alterações simultâneas de outros trabalhos mudem os arquivos durante a validação.

| Validação | Resultado | Evidência |
| --- | --- | --- |
| Geometria real no Editor | 34 carros, cinco posições de esterço por carro, todos aprovados | `editor/geometry.json`, `editor/validate-geometry.cs.txt` |
| Inspeção visual | 21 capturas: frente, cabine e esterço dos cinco modelos, da viatura e do caminhão dos bombeiros | `editor/after-*.png` |
| Preservação da cena no ajuste | 68 linhas alteradas, exclusivamente posição e rotação dos 34 volantes | `editor/scene-changes.json`, `backups/Before-steering-20261108-071953.unity` |
| Controles e direção no player B | 146/146 verificações; encerramento com código 0; nenhum erro de execução | `controls-default/verification.json`, `controls-default/driving-qa.json` |
| Suíte completa no player A | 153/161 verificações; código de saída 1; oito falhas nos cenários de impacto com objetos | `native-default/verification.json`, `native-default/driving-qa.json` |

A rodada nativa de controles usa resolução 1280 × 720 e a API padrão, Direct3D 12, na RX 9070. Inclui os modelos matemáticos dos veículos e, com o sedã físico, entrada e saída por E, aceleração, curvas A/D, contraesterço, freios, ré, suspensão, colisão com parede e pausa por Esc. A checagem geométrica cobre todos os carros, incluindo os veículos de serviço; a validação física de condução desta tarefa usa o sedã.

As oito falhas da suíte completa dizem respeito à conservação de movimento ao atingir objetos leves, resistentes, compostos ou sucessivos, incluindo contatos diagonais, marcha à ré e retomada após pausa. Elas continuam registradas como falhas; o resultado da suíte completa não foi convertido em aprovação. O player B acrescenta a seleção opt-in `--city-driving-controls-qa`, identificada por `controlsOnly: true` no relatório. A suíte `--city-driving-qa` continua executando os impactos. Entre A e B mudou somente o script do probe, conforme `snapshot-controls-source-diff.json`.

O SHA-256 de `Assembly-CSharp.dll` do player B é `A1EBE02BB729878EAD865621C5FD32A5840C87FCBBA51F6156C409DB9F788E31`. `frozen-player.json` registra o caminho do executável, a data, os resultados e os hashes dos 149 arquivos entregues. Os registros `snapshot-controls-build.json` e `snapshot-controls-process-exit.json` documentam a compilação e seu encerramento; `snapshot-sources.json` e `snapshot-controls-source-diff.json` identificam as fontes congeladas. Os relatórios nativos são novos e confirmam que o assembly não mudou durante cada execução.

Para repetir a rodada focada, a partir da raiz do repositório:

```powershell
& ./Unity/Reports/steering/run-driving.ps1 -ControlsOnly
```

O probe é opt-in, usa armazenamento de QA isolado e permanece ausente em uma partida normal.
