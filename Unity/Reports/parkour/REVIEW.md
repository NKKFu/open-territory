# Salto, aterrissagem e parkour

Verificado no player Windows em 2026-11-07, às 23:19 UTC.

O salto ganhou uma preparação de 65 ms, altura proporcional à velocidade de saída, pernas alternadas e braços que mudam durante a subida e a descida. As pernas se estendem antes do contato. A aterrissagem conserva a velocidade real do impacto para flexionar joelhos, quadril e tronco, com recuperação amortecida. Sentar, embarcar e começar a nadar cancelam a preparação pendente.

O parkour procura a frente do obstáculo em três alturas e considera distância, velocidade, ângulo e largura. Há aproximação até o ponto de impulso, passagem sobre barreiras estreitas e subida em superfícies largas. Cada trajetória verifica o volume inteiro do corpo, o teto e cinco pontos de apoio na chegada. Mãos acompanham apoios móveis lentos; velocidade excessiva, espaço insuficiente e chegada sem apoio impedem a travessia. Um obstáculo novo interrompe o movimento com retorno seguro ao controlador. As poses de parkour têm pernas próprias e braços de equilíbrio, sem serem sobrescritas pela pose de salto livre.

Foram aprovadas **248 verificações** na mesma compilação:

- [95 de salto e parkour](native-final/parkour-qa.json): parado/andando/correndo, queda de dois metros, comando antes da aterrissagem, tolerância nas beiradas, cancelamento ao mudar de estado, barreiras de 0,60/1/2 m, aproximações próximas/distantes, corrida, diagonal de 28°, superfícies largas, teto baixo, chegada bloqueada, pausa por Esc, interrupção, apoios móveis, vão sem apoio e um carro real do mapa.
- [121 de passadas e NPCs](gait-final/gait-qa.json): apoio dos pés, cadência, articulações, sons, caminhada/corrida, curva, mira lateral, rampa, parada, salto, pausa e moradores de alturas diferentes.
- [32 de controles e câmera](controls-final/qa.json): movimento, aceleração, frenagem, normalização diagonal, órbita, salto, colisão, mira e pausa.

Os três processos terminaram com código zero. Os logs finais não registraram exceções. As verificações de medição usaram `-nographics`; não alteraram o relógio ou as configurações da partida. Cada pasta contém `verification.json` com o hash do assembly antes e depois da execução.

| Movimento | Altura máxima acima do piso (m) | Impacto vertical (m/s) | Compressão de aterrissagem |
| --- | ---: | ---: | ---: |
| Salto parado | 0,58 | 4,81 | 0,48 |
| Salto andando | 0,62 | 5,03 | 0,50 |
| Salto correndo | 0,71 | 5,45 | 0,54 |
| Queda de dois metros | 2,00 | 9,42 | 0,94 |

A revisão gráfica foi feita separadamente em [visual-final](visual-final/verification.json), com 92 verificações aprovadas e capturas inspecionadas da [subida do salto](visual-final/jump-idle-rise.png), [aterrissagem](visual-final/jump-idle-land.png), [passagem sobre barreira](visual-final/vault-walk-traverse-1.png), [subida alta](visual-final/climb-high-traverse-1.png) e [carro](visual-final/actual-car-traverse-1.png). Essa compilação visual antecede o ajuste de cancelamento da preparação ao trocar de estado; seu hash está no relatório próprio.

Executável final: `Unity/OpenTerritory/Builds/WindowsRelease/BaiaDoSol.exe`.

Assembly SHA-256: `14C65ABE263FBFF885648CE20A955F11AB571141FF85599F34D3607A827CE6F5`.

Os cenários dirigem o controlador e o rig reais em um piso temporário isolado; o teste de carro usa as colisões reais de um veículo do mapa. A cobertura não representa uma auditoria de todos os obstáculos da cidade. O probe é instalado apenas com `--city-parkour-qa` ou por anexação explícita no Editor e usa armazenamento de QA. Reprodução: `powershell -NoProfile -ExecutionPolicy Bypass -File Unity/Verificar-Parkour.ps1 -NoGraphics`.
