# Auditoria dos sólidos externos — 2026-11-06

## Escopo e reprodução

Revisão sistemática dos **tipos de construção externos** em `game.js` e `city-landmarks.js`, incluindo todos os distritos gerados pelos mesmos construtores, oito quiosques da orla, oito casas do Jardim, ambas as praças e o píer. Nenhuma alteração no README, interiores, câmeras, NPCs, mercado de peixe ou implementação do parkour. Sem commit. O repositório já tinha alterações de outros trabalhos; elas foram preservadas.

Execute da raiz: `node tests/external-solids-audit.mjs`.

Requer o Playwright e seu Chromium instalados. O script inicia um servidor HTTP numa porta efêmera e um **Chromium headless isolado**, sem conectar às abas do usuário. Carrega a aplicação real, acrescenta uma API de auditoria somente à resposta HTTP de `game.js` (não altera o jogo salvo), consulta a colisão e o piso reais e envia W pelo teclado do navegador. Encerra servidor e navegador ao terminar. `results.json` e quatro PNGs nesta pasta são regenerados.

## Incidentes corrigidos

| Tipo/local | Falha observada na construção | Correção |
|---|---|---|
| Expositores de pranchas, orla | Collider 3,85 × 0,98 maior que a prateleira 3,58 × 0,54; bloqueava espaço vazio | Collider acompanha a madeira; vãos e pilares continuam separados |
| Quiosque da praça menor (104,112) | Corpo 3 × 2 × 1,3 sem collider | Collider 3 × 1,3 no corpo; teto e passagem lateral sem barreira cheia |
| Estrutura da área de areia da praça menor | Dois pilares de 3,2 m sem collider | Colliders de 0,18 × 0,18 apenas nos pilares; vão central livre |
| Área de areia da praça menor | Degraus desenhados não registrados como piso | Superfícies caminháveis registradas nas dimensões e cotas reais: 7 × 4 a 0,375 e 6 × 3 a 0,56 |
| Cercas das oito casas do Jardim | Painéis de 5 m e postes atravessáveis | Painéis finos e postes registrados separadamente, com alturas reais para o consumidor existente de `vaultTop`; nenhuma barreira nas entradas |
| Placas das vias dos distritos e quatro placas de rua | Hastes visíveis sem colisão | Somente hastes 0,13 × 0,13 / 0,12 × 0,12 registradas; área sob os letreiros permanece livre |
| Dez amarras do píer | Postes atravessáveis | Collider de 0,30 × 0,30 em cada corpo; sem cerca contínua entre eles |
| Lago do Jardim | Retângulo 22 × 10 bloqueava terra seca nos cantos e deixava lóbulos de água sem proteção | Faixas inscritas de 10 cm acompanham o polígono visual de 14 lados; sem trocar o desenho da água nem alterar a função global de colisão |

## Lista de cobertura por tipo

`Fonte` abaixo indica inspeção de geometria/registro; `navegador` indica sondas executadas na cidade real. A revisão por construtor cobre suas instâncias nos distritos; não equivale a dirigir e caminhar manualmente ao redor de cada instância.

| Família externa | Cobertura e decisão |
|---|---|
| Casas fechadas, fachadas residenciais e casas do Jardim | Fonte: corpo sólido acompanha volume principal; portas cenográficas fechadas. Navegador: oito casas e suas entradas de jardim. Cercas corrigidas |
| Fachadas de prédios entráveis, vitrines, portas, floreiras e bancas de fruta/flor | Fonte: paredes segmentadas preservam porta central; corpos das bancas/floreiras já registrados. Interiores e comportamento das portas pertencem ao outro chat; não alterados |
| Árvores urbanas e palmeiras | Fonte: troncos têm collider próprio, copas altas não viram barreiras ao nível do chão; preservados |
| Árvores da floresta e montanhas | Fonte: cenografia além dos limites navegáveis x=±210 / z=220; não adicionados colliders em copas ou montanhas inacessíveis |
| Bancos urbanos, lixeiras, vasos/canteiros elevados | Fonte: colliders dos corpos; bancos só usam rotações ortogonais nas chamadas existentes. Preservados. Banco do abrigo já corrigido no chat de origem |
| Abrigos de ônibus e bicicletários | Fonte: estrutura e banco segmentados já existentes; sem novo bloqueio do espaço sob o teto ou entre suportes. Correções anteriores preservadas |
| Hidrantes, semáforos e postes de iluminação | Fonte: corpos/haste com colliders e vínculo de destruição existente; preservados. Correções anteriores no Anel preservadas |
| Placas de trânsito, de rua, letreiros suspensos | Fonte: hastes ausentes corrigidas; planos gráficos e letreiros altos continuam decorativos. Navegador: quatro hastes de rua |
| Posto: bombas, cobertura, pilares e totem | Fonte: bomba/corpo e pilares já registrados; teto alto não bloqueia a pista. Correções anteriores dos pilares preservadas |
| Cabines telefônicas, caixas eletrônicos e caixas de correio | Fonte: blocos fechados e respectivos colliders coerentes; navegação: três telefones e três caixas eletrônicos |
| Praça principal: fonte, muretas, sorveteria e carrinho de pipoca | Fonte: corpos já registrados e corredores entre eles livres. Navegador: fonte e ambos os quiosques/carrinhos. Sorveteria anteriormente corrigida preservada |
| Praça menor: areia, pórticos e quiosque | Fonte e navegador: incidentes acima, suporte e vão testados; altura central do piso 0,56 |
| Quiosques/barracas de praia: espreguiçadeiras, guarda-sol, balcão de coco, pranchas | Fonte e navegador: todas as oito instâncias, três variantes; pilares, móveis e vãos sob coberturas separados. Prateleiras corrigidas |
| Mesas e cadeiras | Fonte: as chamadas de `table`/`chair` em `game.js` são internas, não há mesas externas adicionais fora do mercado de peixe. Mesas/barracas do mercado ficam no escopo do agente responsável; não alteradas |
| Farol, deques, píer e amarras | Fonte e navegador: farol já sólido, deques são pisos, amarras corrigidas, centro do píer livre |
| Lago e mar | Fonte e navegador: contorno do lago corrigido; mar fora do píer continua limitado pelo limite navegável existente |
| Terreno cercado | Fonte: cinco segmentos de cerca já registrados com abertura de quatro metros na lateral oeste. Navegador: portão em (149,-103) livre |
| Gramados, flores, tufos, arbustos baixos, lixo, tampas, grelhas e juntas de piso | Fonte: decoração pequena deliberadamente não bloqueante; não receberam barreiras retangulares |
| Barcos, acessórios de telhado, varandas e detalhes elevados | Fonte: cenografia fora do acesso a pé ou acima do espaço de circulação; não ganharam colliders verticais infinitos |

## Evidências e resultados

- `node --check game.js` e `node --check city-landmarks.js`: passaram.
- Navegador Chromium com WebGL SwiftShader: **90 sondas passaram**, cobrindo sólidos e passagens; **0 erros JavaScript**.
- Comparação independente com o polígono do lago: **8.459 pontos passaram**, dentro e fora da água, excluindo uma faixa de 15 cm junto à borda para tolerar a aproximação por faixas de 10 cm. O teste não usa a função que constrói os colliders para determinar o contorno esperado.
- Teclado real W: de (104,115) até (104,113,0707), parando antes da face do quiosque em z=112,65 com o raio do jogador. O script exige aproximação e parada, não apenas uma posição final estática.
- Capturas inspecionadas: `small-plaza.png`, `shore-kiosks.png`, `pond.png`, `pier.png`.
- Valores completos e coordenadas das sondas: `results.json`.
- A primeira tentativa do teste de W alterava `yaw` sem `targetYaw`, e a interpolação redirecionava o personagem. O harness foi corrigido para definir ambos; não houve alteração de câmera no jogo para fazer o teste passar.

## Exceções e limites explícitos

- O mar usa a limitação de mundo já existente: fora do píer, z < -163 bloqueia circulação; o deque alcança -179, mas a borda navegável existente termina em -178. Não alterei limites globais, natação ou acesso aos barcos cenográficos. A diferença da borda do píer pertence a essa limitação existente, não a um novo collider invisível de objeto.
- Faixas inscritas do lago aproximam a borda em até a ordem de 10 cm; a banda de exclusão do teste é 15 cm. Não há grande retângulo invisível ocupando os cantos secos. O raio padrão do jogador é maior que essa aproximação.
- Pequenas saliências (tampas de lixeira, acabamentos de fachada, braços de hidrantes, ponteiras de amarra) não foram convertidas em grandes barreiras; a revisão priorizou corpos grandes e vãos utilizáveis.
- Não foi validada uma volta completa de carro ao redor de cada objeto, nem todas as interações E, física de salto ou estados de destruição. Esses comportamentos globais não foram alterados. O teste de movimento exercita a colisão normal do jogador; as sondas exercitam a mesma função e os registros espaciais usados no mundo.
- Outros chats continuam responsáveis por interiores/poltrona, câmeras, mercado de peixe, NPCs e parkour. Nenhuma afirmação aqui representa aceitação desses escopos.
