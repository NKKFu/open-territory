# Open Territory

## Automação do Unity

- Priorize CoplayDev MCP for Unity (`unityMCP`) para inspecionar cenas, GameObjects, prefabs, Console, recompilação e Play Mode. Use ferramentas de arquivos/shell para código e Git.
- Evite Computer Use para Unity. Se o MCP desconectar, diagnostique e reconecte via MCP/CLI/scripts; não recorra a cliques/teclado automatizados. Use Computer Use apenas se o usuário pedir explicitamente.
- Endpoint local: `http://127.0.0.1:8087/mcp`, configurado em `.codex/config.toml`. Se necessário, execute `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Start-UnityMcp.ps1`. O bootstrap do Editor conecta ao carregar/recompilar.
- Direcione comandos à instância OpenTerritory; outros projetos Unity podem estar abertos. Antes de mudar de cena ou entrar em Play Mode, confira a cena ativa e alterações não salvas. Preserve o trabalho do usuário e encerre Play Mode após testes.
- Após recompilar, espere o Editor ficar pronto e valide a reconexão. Não desative domain reload nem altere configurações de gameplay para fazer o MCP passar. Separe erros do jogo de falhas da conexão.
- Confirme que o assembly do Editor é posterior aos scripts alterados; quando necessário, confira os membros novos por reflexão. Uma resposta de refresh ou ausência de erros antigos no Console não comprova que os tipos novos já foram carregados. Se o refresh condicional deixar scripts pendentes, force o refresh, espere a recompilação e confira novamente antes do build.
- CoplayDev está fixado no commit da versão v10.3.0; não atualize silenciosamente para uma branch móvel.

## Desempenho e ciclo de vida

- Antes de otimizar, registre uma referência no player nativo e repita o mesmo cenário depois. Preserve resolução, qualidade, API gráfica, população, semente, aquecimento e opções de build; execute as rodadas em sequência, sem disputar CPU/GPU com outros testes. Separe a cidade em movimento dos cenários de estresse e dos custos de uma operação isolada.
- Compare tempo por quadro e seus percentis, alocações gerenciadas e memória; use os contadores disponíveis de CPU, GPU e renderização. Um contador ausente ou zerado por falta de suporte é indisponível, não um custo zero. Não prometa ganho de FPS a partir de uma redução de alocações ou de um microbenchmark.
- Instrumentação, cenas auxiliares e retirada do limite de quadros pertencem somente aos probes opt-in. Não altere VSync, qualidade, preferências persistidas ou controles da partida normal para melhorar números de QA. `Verificar-Desempenho.ps1` usa `--city-performance-qa` e armazenamento isolado.
- Em buscas de rotas, reutilize o espaço de trabalho por instância do serviço e evite criar arrays/listas a cada solicitação. Preserve filtros de arestas bloqueadas, travessias, alcance físico dos nós e a preferência estável por trajeto. Valide sucesso, falha, recuperação e buscas sucessivas; nenhum resultado pode herdar estado de uma busca anterior.
- Pools devem ter capacidade explícita, preparação fora do evento de impacto e uma política previsível de saturação. Reutilize malhas e materiais; não crie GameObjects, componentes ou materiais para cada efeito. Ao devolver um corpo ao pool, elimine velocidade e rotação residuais e desative sua participação física; ao obter, restaure pose, escala, aparência, filtros de colisão e prazo de vida.
- Use o relógio da simulação para a vida útil dos efeitos do mundo. Esc deve congelar movimento e expiração; retomar ou reciclar um efeito não pode reaproveitar um vencimento antigo. Teste saturação, reutilização e devolução completa do pool no executável.
- Registre caminho do executável, hash do assembly, configuração, data e relatório novo de cada validação. Um relatório antigo ou um player de outro build não comprova a alteração atual. Em trabalhos simultâneos, valide e entregue em uma pasta de build exclusiva; o executável compartilhado pode mudar durante a tarefa. Preserve a cena e as mudanças de outros trabalhos; não reconstrua a cidade para uma otimização somente de código.
- Confira também o código de saída do processo: um JSON com `passed=true` não comprova encerramento sem falha. Se uma API gráfica falhar já na referência, preserve o diagnóstico e identifique a API nas comparações; valide a configuração padrão separadamente e não misture resultados de APIs diferentes.
- Para comparar uma alteração somente de código durante mudanças simultâneas no mapa, use uma cópia do player de referência e um build `BuildScriptsOnly`, conferindo o hash dos dados do player. A entrega final requer build completo e validação do cenário atual; um build de scripts não entrega alterações de assets ou de cena.
- Congele também os blobs externos de áudio/textura e os metadados do player: apenas o hash da cena é insuficiente. `Preparar-Comparacao-Desempenho.ps1 -RestoreContent` restaura o conteúdo comum após cada variante. As implementações históricas para A/B ficam atrás do define exclusivo de build `CITY_PERFORMANCE_REFERENCE`, sem modificar preferências nem os defines persistidos do projeto.

## Implantação de construções

- Antes de adicionar ou mover uma construção, confira a área completa que ela ocupará, incluindo muros, portões, jardins, quintais, calçadas, mobiliário, corredores e acessos. Não use apenas a distância entre os centros dos prédios nem somente a lista de imóveis como prova de espaço livre.
- Preserve as construções e os espaços existentes. Se houver sobreposição, reposicione a construção nova; não remova nem esconda prédios, jardins, muros ou seu mobiliário para fazê-la caber.
- Inspecione a geometria e as colisões reais no Unity e verifique também os dados de ruas e lotes. Garanta uma ligação contínua e desobstruída com a calçada, sem invadir a rua ou bloquear portas e passagens de outros locais.
- Antes de entregar, confira visualmente o conjunto de cima e no nível do personagem e percorra a entrada, os caminhos e o interior no Editor ou executável. Corrija qualquer sobreposição ou obstrução encontrada.

## Interface do jogo

- O idioma de apresentação do jogo é inglês: falas, menus, interações, avisos, mapa e placas. Escreva novos textos visíveis em inglês e mantenha os identificadores internos e os créditos originais dos autores.
- A interface deve ter desenho próprio e acabamento de produto: hierarquia visual, tipografia legível, espaçamento, estados de interação e componentes coerentes com Baía do Sol.
- Não use IMGUI/`OnGUI`, `GUI`, `GUILayout`, `GUISkin` nem janelas e controles com a aparência padrão do Unity no jogo. Não entregue textos crus ou botões de depuração como interface final.
- Use Canvas/uGUI ou UI Toolkit com composição e estilos próprios. As ferramentas do Editor e os probes de teste são separados da interface que o jogador vê.
- Use pouco texto nas interações: prefira verbos curtos, como "Open", "Enter" e "Talk". Deixe o que for óbvio implícito nas animações, no som e nas mudanças do mundo; evite tutoriais permanentes e confirmações que apenas repetem a ação.
- Mostre explicações só quando necessárias para entender uma ação incomum, um bloqueio, um custo ou um risco. Preserve falas naturais e informações essenciais, sem narrar cada reação dos moradores.
- Mantenha avisos e painéis pequenos, ajustados ao conteúdo, sem grandes áreas vazias. Preserve a legibilidade e a área visível do cenário; rótulos e atalhos devem ocupar apenas o espaço necessário.
- O hacking deve indicar o alvo e a ação em um painel contextual compacto, ligado ao objeto, com suporte a teclado, mouse e toque. Só explique consequências que não sejam claras pela ação. Preserve a exploração sem HUD permanente, minimapa ou pausa da cidade; Esc continua sendo a exceção para configurações.
- Valide a interface no executável/editor, incluindo legibilidade, posicionamento, oclusão, navegação, estados indisponíveis e os controles afetados.

## Projeto

O projeto nativo atual fica em `Unity/OpenTerritory` (Unity 6000.3.27f1), com a cena `Assets/Scenes/BaiaDoSol.unity`. Leia `Unity/README.md` antes de alterar a versão Unity. Valide mudanças de jogabilidade no executável/editor, incluindo os controles e interações afetados. Os probes de execução são opt-in e não podem rodar em uma partida normal. Preserve as configurações acessíveis por Esc e a tela cheia solicitadas pelo usuário; a pausa de configurações é uma exceção às interações contínuas do mundo.

Os arquivos web da raiz permanecem como referência e fonte do conversor offline de geometria. O player Unity não depende deles em execução. Para trabalhar especificamente nessa referência:

O jogo está na raiz deste repositório. Execute `python3 -m http.server 8790 --bind 127.0.0.1` ou abra `servir.command` no macOS. O Three.js está incluído em `vendor/`.

Preserve exploração livre, sem menu inicial, HUD, minimapa, missões ou objetivos. Falas e informações aparecem temporariamente no próprio mundo, sem diálogos modais ou pausa da simulação. O visual é 3D low poly quadrado. Os interiores ficam no mesmo mundo; portas normais abrem ao empurrar e algumas são automáticas. Priorize movimento suave, colisões consistentes, trânsito que respeite pedestres e disposição plausível dos objetos.

Controles e limitações atuais estão em `README.md`. Verifique mudanças de jogabilidade no navegador, incluindo controles e interações afetadas.
