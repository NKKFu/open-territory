# Cercas e árvores destrutíveis

Construção completa: `../../Deliveries/OutdoorDestruction-20261108/BaiaDoSol.exe`, aberta por `../../Jogar-Destruicao.ps1`.

- 2.964 trechos ativos de cerca e 7.115 árvores registrados na cena validada, incluindo os quintais, a floresta, o cemitério e o parque.
- Tiros, golpes e contatos físicos de veículos usam o sistema de dano existente. Partes sobrepostas da mesma divisa recebem o mesmo impacto, em um único salto, preservando os trechos seguintes.
- As cercas conservam os lotes de renderização. A destruição remove somente os índices da seção atingida e suas colisões; as malhas importadas permanecem imutáveis.
- Árvores tombam com a própria malha, usando um corpo do pool preparado. Estilhaços, folhas e árvores caídas ignoram personagem, moradores e veículos. O limite continua em 72 corpos, com reciclagem do mais antigo na saturação.
- Esc congela física e expiração. A vida dos efeitos usa oito segundos do relógio da simulação. A reutilização restaura massa, malha, material, forma de colisão, pose, escala, velocidades e prazo.
- A cena não foi reconstruída por esta alteração. Os componentes e as cópias mutáveis de malha são preparados na inicialização da partida. O probe só é instalado por `--city-outdoor-destruction-qa`.

## Validação nativa

173 verificações passaram em sequência, no mesmo assembly e com saída 0 em todos os processos:

| Rodada | Verificações | Relatório |
| --- | ---: | --- |
| Destruição exterior, incluindo passagem real, 24 variantes de árvore, armas, golpes, carros, pausa e pool | 56 | `native-verified/verification.json` |
| Regressão de impactos, incluindo objetos compostos, resistência, paredes, ré, pausas e freios | 113 | `impact-regression/verification.json` |
| Sessão comum, movimento, configurações e encerramento | 4 | `normal-session/verification.json` |

As três sessões usaram Direct3D 12. As preferências do jogador foram isoladas pelos modos de QA; a API e a resolução do teste não alteram a configuração normal da partida. Não foi medida uma comparação de FPS.

`verification.json` reúne o caminho do executável, o SHA-256 do assembly e os relatórios novos. `build.json` identifica a construção completa. `delivery-manifest.json` registra tamanho e SHA-256 de todos os arquivos da entrega. As capturas em `native-verified` foram conferidas visualmente. `editor-passage.json` registra a reprodução do problema das divisas sobrepostas e a passagem após a correção, com Play Mode encerrado.

As rodadas anteriores ficam preservadas como diagnóstico: a primeira revelou a sobreposição entre divisas e a captura de tela indisponível em batch; uma construção intermediária aguardava os prefabs de armas de uma alteração simultânea. A entrega corresponde somente às três rodadas aprovadas acima.

Após a primeira aceitação completa, a pasta compartilhada `OpenTerritory/Builds` ficou vazia. A entrega foi reconstruída em `Deliveries/OutdoorDestruction-20261108`, e as três rodadas foram repetidas nessa cópia, com 173 aprovações, saída 0 e o mesmo assembly entre elas. `Jogar-Destruicao.ps1` aponta diretamente para essa entrega.
