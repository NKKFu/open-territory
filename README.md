# Open Territory — Baía do Sol

Um jogo de exploração 3D em low poly, inspirado em Ilhas Mágicas. O jogo abre diretamente na cidade, sem menu inicial, minimapa ou objetivos.

O mapa nativo atual substitui a cidade antiga por uma costa traçada das quatro capturas de Marin: contorno, autoestrada, ruas e trilhas usam pontos de referência fixos. Os prédios, interiores, móveis, vegetação, moradores e veículos do mapa anterior foram reaproveitados e redistribuídos. A biblioteca de origem fica fora da cena jogável, em `Unity/AssetLibrary/OriginalCity`.

## Abrir o jogo

### Unity / Windows

Após clonar, execute `python Unity/Tools/large_assets.py restore` na raiz antes de abrir o projeto Unity. Para registrar alterações nos arquivos grandes, siga as instruções de `status` e `pack` no [guia Unity](Unity/README.md).

O projeto nativo está em **`Unity/OpenTerritory`**, para Unity **6000.3.27f1**. Abra `Assets/Scenes/BaiaDoSol.unity` no editor e pressione Play, ou execute **`Unity/OpenTerritory/Builds/WindowsRelease/BaiaDoSol.exe`**. A simulação roda em C#, com os modelos low poly e interiores na mesma cena.

Veja [instruções da versão Unity](Unity/README.md) para controles, estrutura, compilação e reconstrução dos assets. A versão web abaixo permanece como referência e fonte do conversor de geometria.

Na versão Unity, **M** abre o mapa interativo: arraste para navegar, use a roda para zoom e clique para marcar um lugar. **C** centraliza sua posição; **M/Esc** fecha o mapa. O guia mostra locais da cidade e paradas de ônibus, mantendo a simulação em movimento.

Na versão Unity, **X** guarda a arma; com as mãos livres, **clique esquerdo/B** dá socos em sequência e **botão direito/G** mantém ou alterna a guarda. Três acertos seguidos no mesmo alvo derrubam temporariamente. Os controles de toque incluem **Soco** e **Guarda**.

O áudio nativo tem 84 sons originais: ambiente costeiro estéreo, passos conforme o piso, motores ligados à rotação e efeitos espaciais abafados por paredes. **Esc → Som** permite ajustar volume geral, efeitos, ambiente e música/rádio. `Unity/Verificar-Audio.ps1` testa a saída real de áudio no executável.

### Referência web

No macOS, dê dois cliques em `servir.command`. Ele abre o jogo no navegador e mantém um pequeno servidor local no Terminal. Para fechar o servidor, feche a janela do Terminal ou pressione Ctrl+C nela.

Também é possível iniciar manualmente, dentro desta pasta:

```sh
python3 -m http.server 8790 --bind 127.0.0.1
```

Depois acesse `http://127.0.0.1:8790/`. Se a porta estiver ocupada, use outra. O arquivo `index.html` precisa ser servido por HTTP por usar módulos JavaScript; o Three.js já está incluído em `vendor`, sem depender de download.

Na qualidade automática, o jogo adapta a resolução interna e as sombras ao dispositivo. As opções Leve e Alta permitem escolher diretamente entre sombras de contato e sombras dinâmicas.

Os modelos fora da vista deixam de entrar nos lotes de desenho, preservando os que ainda projetam sombras visíveis. As rotinas continuam simuladas. Veículos parados reutilizam o apoio das rodas depois que a suspensão estabiliza e voltam a calculá-lo quando se movem.

O ambient occlusion acrescenta sombras suaves nos encontros de paredes, móveis, personagens e chão. Ele acompanha a câmera e os objetos em movimento, rejeita sombras falsas sobre superfícies planas e suaviza o resultado preservando as bordas. Navegadores sem os recursos WebGL necessários mantêm a iluminação normal.

A paleta tem vegetação, fachadas e roupas mais coloridas, com um ajuste leve de contraste e saturação. Uma vinheta suave escurece as bordas sem reduzir a visibilidade do centro. Asfalto, reboco, calçadas, madeira, grama, areia e água usam texturas geradas no próprio jogo em um atlas de 512 × 256 pixels. Os detalhes mantêm uma escala coerente entre objetos de tamanhos diferentes; os quarteirões continuam compartilhando os lotes de desenho.

Os sons começam no primeiro clique ou comando de teclado, respeitando o bloqueio de reprodução automática do navegador. Há vento, mar perto da praia, aves durante o dia, passos diferentes no piso e na grama, motores com volume e posição conforme a distância, portas e impactos. O áudio é gerado no próprio jogo, sem arquivos ou serviços externos. O volume também pode ser controlado ou silenciado pela aba do navegador.

## Controles

- Use **WASD ou as setas** para andar, **Shift** para correr e **Espaço** para pular. Em piso plano, os pés sobem cerca de 32 cm num salto parado e 46 cm após ganhar velocidade. A câmera mantém o horizonte estável durante o salto.
- **Alt + Enter** alterna a tela cheia. Também dá para usar o botão em **Esc → Imagem** ou o ícone de tela cheia ao lado da engrenagem no toque.
- **V** lança o drone de reconhecimento a pé. Pilote com **WASD**, arraste para girar a câmera e use **Espaço / Shift** para subir e descer. Segure e solte **F** para acionar um dispositivo visível. **E** ou **V** devolve o controle ao personagem enquanto o drone retorna. No toque, use **Drone**, o direcional e os botões **Subir / Descer**.
- Caminhe da praia para o mar para nadar. **WASD** muda o rumo, **Shift** acelera a braçada e soltar o direcional deixa o personagem boiar. No toque, toque duas vezes numa direção e mantenha o segundo toque. Dá para sair pelo raso ou subir com **E** nas escadas do píer e do cais.
- Use **E** junto ao casco para embarcar da água ou do deque. A bordo, **W / S** acelera e dá ré, **A / D** controla o rumo e **Espaço** freia. **E** pede a parada e desembarca no cais ao alcance ou na água. No toque, use o direcional, **Freio** e **E**.
- **Parkour:** segure uma direção e aperte **Espaço** ao chegar a uma mureta ou à cerca baixa do terreno vago. O personagem apoia a mão, recolhe as pernas e aterrissa do outro lado. Num carro parado, aproxime-se pela lateral do capô para transpô-lo ou mire na cabine para subir ao teto. Há duas muretas na borda da praça inicial, perto do carro amarelo.
- A câmera acompanha o rumo do personagem ao caminhar e correr. Segurar uma direção mantém o trajeto enquanto ela gira. Arraste com o mouse ou o dedo para olhar ao redor. Parado, o ângulo escolhido fica livre. Use a roda do mouse para ajustar a distância.
- **Segure F** a pé para levantar o celular e localizar um dispositivo em até 24 metros. Mire com a câmera e solte F para acionar o alvo marcado. Num semáforo, o fluxo aberto passa pelo amarelo e os dois sentidos ficam vermelhos por oito segundos naquele cruzamento. Veículos que já passaram da linha liberam a faixa. Portas automáticas abrem por oito segundos, carros acionam o pisca-alerta por cinco segundos e freiam se estiverem sob controle do trânsito, e o painel do quartel alterna a água. No toque, segure o botão F.
- Caminhe contra uma porta para empurrá-la. As do hospital e do posto são automáticas. A câmera passa pelo vão e se aproxima nos lugares apertados. Telhado, paredes, batentes e janelas continuam visíveis durante a travessia.
- Há câmeras públicas na Praça do Ipê e no Hospital Baía. Mire na lente e solte **F** para assumir o controle. Arraste a imagem ou use o direcional para girar a lente. Use **F** para acionar um dispositivo visível dali e **E** para voltar ao personagem. Dá para tocar um telefone público e observar os moradores reagindo ou abrir a porta do hospital à distância.
- Quando houver uma interação disponível, **E** aparece junto ao alvo com o que você pode fazer, como **Conversar**, **Sentar** ou **Entrar no carro**. Em telas de toque, use o botão E. Ao conversar, o morador para, olha para você e faz um gesto. Aperte E por perto para ouvir outra fala. Ele retoma o passeio quando a conversa termina ou você se afasta.
- **Ônibus circular:** espere junto à porta na parada e pressione **E** para embarcar. O ônibus abre e fecha as portas, aguarda os passageiros e percorre quatro paradas. Durante a viagem, **E** solicita desembarque na próxima parada e exibe uma confirmação no mundo. No ponto, E desembarca assim que houver espaço livre; se a saída estiver ocupada, o ônibus aguarda. Sem solicitar saída, você continua a viagem.
- **Ao dirigir ou pedalar:** W acelera, S freia e engata a ré, A/D viram à esquerda/direita, espaço freia e E sai. No carro, Q liga/desliga a seta esquerda e Z a direita. Nas viaturas e no caminhão de bombeiros, L liga/desliga as luzes de emergência. Aproxime-se de uma porta lateral e pressione E para entrar em um carro parado; se houver motorista, ele sai primeiro quando há espaço. Ao pedir para sair em movimento, o veículo freia antes de liberar a saída pelo lado de entrada ou pelo lado oposto livre. A câmera acompanha o veículo; arraste para olhar ao redor.
- **1 / 2 / 3** equipam o Pulsor Maré, a P-9 Farol ou a C-12 Orla, se você os possui. **X** guarda a arma. Segure o **botão direito** ou aperte **G** para mirar; aponte e dispare com o **botão esquerdo**. **R** recarrega. No toque, **Arma** alterna entre os equipamentos e as mãos livres; toque em **Mirar**, depois no alvo ou em **Atirar**. Para continuar usando o celular, guarde a arma ou saia da mira.
- **R com as mãos livres:** experimentar uma queda. O personagem levanta sozinho após alguns segundos.
- **H** toca a buzina do carro ou a campainha da bicicleta. Os controles de toque também têm esse botão.
- Ao volante, **Espaço** é o freio de serviço. **Shift** segura o freio de mão nas rodas traseiras. No toque, use **Freio** e **Mão**. Segurar **S** primeiro imobiliza o carro e depois engata a ré. Um toque curto nas teclas de direção faz uma correção menor do que mantê-las pressionadas.
- No carro, na bicicleta e no ônibus, a câmera acompanha as curvas e enquadra o caminho à frente. Manter a ré faz a vista se voltar para o sentido da manobra. Ao dirigir, você também pode arrastar a câmera para conferir uma lateral.
- **Esc:** abrir as configurações e pausar a cidade. Ajuste a qualidade gráfica, o campo de visão, o volume, a sensibilidade e a inversão da câmera. As escolhas ficam salvas neste navegador. Aperte Esc novamente ou escolha **Voltar ao jogo** para retomar. Em telas de toque, use a engrenagem.

Em telas de toque, arraste o cenário para controlar a câmera. Toque duas vezes numa direção e mantenha o segundo toque para correr. O parkour usa a direção e o botão de pulo juntos.

O personagem muda a passada entre caminhada e corrida, recolhe os joelhos no ar e amortece a chegada ao chão. Ao acariciar, ele dá o último passo, se abaixa à altura do animal e passa a mão sobre sua cabeça. A câmera enquadra os dois; você pode girá-la durante o carinho ou sair andando para interrompê-lo.

Os personagens conservam as formas quadradas e os materiais foscos. Há oito combinações de roupas e cabelo, com golas, bolsos, mochilas, óculos, orelhas, nariz e sobrancelhas. Cotovelos, joelhos e tornozelos se articulam. O tronco acompanha a partida, a parada e as curvas; os moradores apoiam os pés e seguram livros, copos, celulares e ferramentas com as mãos. A mesma linguagem visual aparece nos passageiros, motoristas e ciclistas.

Dentro dos prédios, você pode se sentar com **E**. Aproxime-se pela frente das poltronas, sofás e cadeiras de espera, ou pelo lado de uma cadeira à mesa. O personagem conserva sua roupa e apoia os pés no chão. Pressione **E** ou ande para levantar quando houver espaço livre.

## O que há na cidade

Na avenida das Colinas, **a Oficina Farol** vende e recompra equipamento. Você começa com R$ 320 e um Pulsor Maré, que atordoa sem tirar vida. A P-9 é semiautomática (R$ 180) e a C-12 faz disparos automáticos (R$ 360). Aproxime-se de uma vitrine: o preço aparece no próprio **E**. Se a arma já for sua, a vitrine repõe munição e só cobra pelo que cabe na reserva. Para vender, procure as bandejas identificadas no balcão de **Recompra**; a munição acompanha a arma. Os caixas eletrônicos e o caixa da oficina mostram o saldo. Dinheiro, equipamentos e munição ficam salvos neste navegador.

No pátio ao lado da loja, o **Estande Farol** tem três alvos articulados. Os disparos feitos das faixas amarelas que terminam no anteparo contam como treino. O alvo balança e soa quando atingido; confira os acertos na ficha à entrada. Disparar para fora ou contra alguém não é treino. Na Praça do Ipê, um guia de bairro indica a oficina, o Hospital Baía e o galpão da peixaria.

Disparos respeitam paredes, móveis, veículos e portas. O ferimento interrompe conversas e pode derrubar um morador. A vida se recupera após um período sem dano; a recepção do **Hospital Baía** também atende com **E**, gratuitamente. Quando a vida do personagem chega a zero, ele fica caído e depois volta a poder se mover.

Se alguém vir uma agressão ou um roubo, pode ligar para a polícia. O som de um disparo também pode atrair uma busca sem identificar quem atirou. Enquanto uma testemunha estiver ligando, é possível mirar no celular dela e soltar **F** para interromper a chamada. Quando os policiais identificam o personagem, tentam abordá-lo. Perder a pista faz com que procurem no último local conhecido. Para se render, saia do veículo, aproxime-se do policial e aperte **E** quando aparecer a indicação. A multa só usa o saldo disponível.

Uma viatura que já circula pela cidade pode participar da ocorrência. Ela segue as ruas, sinaliza a perseguição com giroflex e sirene e repassa a última posição vista; ao encerrar a busca, desliga os sinais e volta à ronda. A cena de revisão `?debug&near=fuga` coloca um carro ocupado e essa viatura na mesma avenida.

Na orla há um farol com luz pulsante à noite, um quiosque de água de coco, um expositor de pranchas e espreguiçadeiras. Na praça, as fachadas residenciais têm varandas, venezianas e floreiras. O mercado expõe frutas, a floricultura vende flores em caixotes e os murais retratam as ondas, uma garça e o casario da baía.

No extremo leste da orla fica o **Cais das Antenas**. Use **E** no console ou **F** pelo celular ou pelo drone para içar o contêiner e abrir passagem sob o guindaste. O sensor para a carga se alguém entrar no caminho. Na **Rádio Onda Curta**, cada sintonia toca uma vinheta e vira as lâminas do mural. Junto à oficina de velas, a manga de vento acompanha as rajadas e o posto meteorológico informa a previsão. A lancha Guará fica no pontão.

A oeste da Galeria Sol fica o **Cine da Passagem**, com bancos ao ar livre. Aproxime-se do projetor e use **E**, ou ligue-o à distância com **F**, para assistir ao curta de um barco atravessando a baía. A chuva também aparece na animação.

O tempo muda durante a exploração. Bruma marítima encurta a visibilidade. Chuva e aguaceiros molham as ruas, aumentam a distância de frenagem e agitam o mar. A chuva soa diferente quando você entra sob um telhado, e o piso seca gradualmente.

Na **Roda do Ipê**, no gramado da Praça do Ipê, Ivo toca violão com frases musicais originais. Moradores próximos podem parar para ouvir, dançar ou bater palmas antes de retomar o passeio. Aproxime-se e use **E · Aplaudir a roda** para acompanhar e pedir outra frase. Começar a andar interrompe o gesto. A música para durante uma conversa com o músico, em perigo ou com chuva forte, e retoma quando a situação permite.

Um drone baixo e visível pode chamar a atenção de quem passa. Alguns moradores olham para ele e outros levantam o celular para fotografar; paredes, distância e altura limitam a percepção. Quando chove, moradores expostos abrem guarda-chuvas ou protegem a cabeça e apressam o passo. Conversas, travessias, embarque, trabalho e perigo têm prioridade sobre essas reações.

No Galpão da Maré, a leste da orla, **E** conversa com o peixeiro ou troca a carga de peixes e os pesos de uma balança de dois pratos. Perto da praia há espuma nas ondas. Ao cair da noite aparecem vaga-lumes e ouvem-se grilos à beira do lago. A fonte tem som posicional e correr junto às árvores levanta folhas.

Para ganhar dinheiro na peixaria, confira o peso pedido na **Expedição**, coloque os peixes da caixa azul e os pesos da caixa de madeira nos pratos e entregue o lote equilibrado com **E** no emissor de recibos junto à balança. O próximo caixote pede outro peso.

Os moradores cedem passagem uns aos outros, esperam espaço na faixa e podem parar para trocar algumas palavras e gesticular. É possível se aproximar e entrar na conversa com **E**. As calçadas e os meios-fios acompanham os limites do asfalto, inclusive nas travessas diagonais.

Antes de atravessar, os moradores olham para os dois lados. Ao perceber um carro vindo na sua direção, podem apressar o passo na faixa ou recuar para um ponto livre na calçada. Uma buzina chama a atenção de quem está perto. Os motoristas desaceleram para a fila e para a travessia, sinalizam as conversões e retomam o percurso quando a passagem libera.

Os cinco carros low poly também podem ser vistos juntos no pátio **Motores Acácias**. O Aurora S é um sedã, a Maré Touring é uma perua, a Caburé tem caçamba aberta, o Faísca é compacto e o Foz Cargo é um furgão. A viatura tem pintura e equipamentos próprios. Cada carroceria tem portas, cabine, eixos e superfícies de apoio ajustados à silhueta. Peso, aderência e motorização influenciam as manobras.

Ruas conectadas à orla e às avenidas laterais, com calçadas, cruzamentos, faixas e semáforos; uma curva na avenida junto à praça inicial, a avenida das Colinas e dois bairros com curvas e diagonais. As novas ruas de mão única têm duas faixas, setas de direção e placas de PARE que fazem o trânsito parar e dar preferência. Há estacionamentos, acessos pavimentados até as entradas, um terreno vazio, praças, lago, relevo suave, bosque, praia, píer e barcos. Árvores, grama, flores, bancos, lixeiras, telefones públicos e caixas eletrônicos completam o cenário.

O café, a galeria, a livraria, o mercado, a padaria, a floricultura, o hospital, a loja do posto, a delegacia e o quartel dos bombeiros têm interiores no próprio mapa, com luminárias que iluminam o ambiente à noite. Casas de moradores, térreos de prédios residenciais e dois restaurantes também podem ser visitados. Os restaurantes têm mesas, clientes e cozinha com fogão, panelas, pia e geladeira.

Os moradores alternam destinos e afazeres: casa, trabalho, comida, leitura, celular, conversas, brincadeiras, descanso e viagens de ônibus. Alguns adultos fumam ao ar livre. As vagas de cada atividade têm capacidade limitada; os moradores seguem calçadas e travessias, empurram portas e suspendem suas rotinas durante uma conversa, retomando-as depois. Nas paradas, quem desce tem prioridade; moradores que usam o circular retomam os afazeres depois de desembarcar. As falas acompanham o que estão fazendo. Alturas, roupas, cabelos, mochilas, chapéus e óculos variam. Há também policiais e bombeiros, trabalhadores, cães, gatos e pombos. Na delegacia, E consulta o quadro de informações; no quartel, E liga ou desliga o painel de treino com água.

Os pombos dão pequenos voos pela praça e reagem em grupos quando alguém corre perto. As asas acompanham o voo e fazem um ruído discreto; o pouso procura espaço livre e espera quando alguém ocupa o local. Os mesmos animais retomam seus passeios depois, sem aumentar a quantidade de modelos na cidade.

Há murais junto ao café, à galeria e a um dos residenciais. Dora pinta ao lado do Café Aurora, faz pausas e continua sua rotina; uma conversa interrompe o trabalho e ela volta a usar o pincel depois. A água tem movimento na superfície, a fonte produz pequenas ondas onde as gotas caem e algumas folhas flutuam com a brisa. Esses efeitos usam conjuntos fixos de instâncias, sem criar novos objetos a cada quadro.

O trânsito mantém distância de outros veículos, para antes das faixas nos sinais vermelhos e freia para pedestres. Os motoristas seguem as rotas usando o mesmo modelo de aceleração e direção do jogador, esterçando as rodas para fazer curvas e mudanças de faixa. Nas vias com duas faixas no mesmo sentido, podem mudar de faixa para contornar um veículo. Nos trechos retos de mão dupla, podem passar por um veículo parado ou objeto na pista quando há espaço, longe dos cruzamentos e sem tráfego ou pedestres na manobra; se necessário, recuam para ganhar espaço e esperam a faixa ficar livre. Nas batidas, os veículos recebem impulsos conforme a velocidade, o peso e o ponto de contato, podendo girar sem atravessar paredes ou pessoas. Após uma batida significativa com o jogador, o motorista para, liga o pisca-alerta, procura uma saída livre, sai para conferir e volta ao carro antes de retomar a rota. Faróis do jogador e do trânsito próximo iluminam o chão durante a noite. Há também bicicletas utilizáveis, ciclo de dia e noite e objetos, inclusive postes e semáforos, que se quebram em fragmentos e partículas ao serem atingidos por um veículo.

Cada roda acompanha o chão, inclusive rampas e meios-fios. Os pneus dianteiros e traseiros usam a aderência do piso embaixo deles. Em uma manobra, a roda da frente do lado de dentro da curva esterça mais. Em velocidade, frear e virar disputam a aderência disponível. O sedã faz curvas mais fechadas em baixa, a picape precisa de mais espaço, e o ônibus e o caminhão usam a distância entre seus próprios eixos. A carroceria inclina ao frear e fazer curvas. Quando os pneus escorregam, o som muda com o piso. O ciclista se apoia no selim, segura o guidão e acompanha os pedais.

A orla fica ao nível do mar; as avenidas sobem até o patamar da oficina e voltam a subir em direção ao Mirante. Os lotes dos prédios permanecem nivelados, enquanto as faixas pintadas, os meios-fios e as quatro rodas acompanham as ladeiras.

A floresta e as boias no mar delimitam a área de exploração. A praia, os dois embarcadouros e as embarcações podem ser visitados. Os andares superiores dos residenciais e os portões de garagem dos serviços são cenográficos. Ao atualizar, as rotinas, o clima, as ocorrências e a saúde recomeçam. Carteira e inventário ficam salvos.

## Verificação

Os testes de reserva de atividades, suspensão de rotinas, pintura e viagens, continuidade fora da câmera, impactos e manobras de veículos, reação dos motoristas a colisões, transformações e texturas dos modelos, apoio das rodas em pisos, meios-fios e encostas e voos dos pombos usam o executor nativo do Node.js:

```sh
node --test tests/*.test.mjs
```

As interações e a navegação do mapa também precisam ser verificadas no navegador.

Para visitar os novos locais, acrescente `?debug&near=motores`, `cais`, `drone`, `cinema`, `nadar`, `lancha` ou `barco-porto` ao endereço. Nessas cenas, `&weather=mist`, `rain` ou `squall` inicia com bruma, chuva ou aguaceiro. Os mesmos locais e mudanças de tempo fazem parte da exploração normal.

Com o servidor aberto, rode `node tests/harbor-flight.mjs http://127.0.0.1:8790` para verificar controle do drone, guindaste, rádio, cine, boletim e tela cheia. `node tests/city-weather-browser.mjs http://127.0.0.1:8790` verifica os efeitos do clima no navegador.

`node tests/marine-controls.mjs http://127.0.0.1:8790` verifica a praia, a natação, os barcos, as escadas e o toque. `node tests/vehicle-showcase.mjs http://127.0.0.1:8790` percorre as cinco carrocerias e verifica suas portas e rodas.

Em um ambiente com Playwright e Chromium instalados, execute `node tests/interior-collisions.mjs http://127.0.0.1:8790` com o jogo aberto nessa porta. O teste verifica os móveis de cada interior e as estruturas da peixaria. Depois ele usa W e E pelos quatro lados da poltrona da livraria. A revisão de quiosques, cercas, píer, lago e passagens externas roda com `node tests/external-solids-audit.mjs`.

Execute `node tests/camera-steering.mjs http://127.0.0.1:8790` para repetir as curvas a pé com teclado e toque e uma curva ao volante.

Execute `node tests/city-commerce.mjs http://127.0.0.1:8790` para conferir compra, recarga de estoque, teste no estande, revenda, persistência, caixa eletrônico, pagamento da peixaria, hospital e toque. Para visitar esses locais diretamente, use `?debug&near=oficina`, `estande`, `expedicao`, `atendimento`, `guia` ou `mirante`.

No mesmo servidor, `node tests/combat-controls.mjs http://127.0.0.1:8790` testa os disparos, a recarga e a cobertura; `node tests/police-patrol.mjs http://127.0.0.1:8790` testa o contato da viatura, a perda da pista e a sirene. Para verificar as ruas renderizadas, correr na subida e dirigir pelo desnível, use `node tests/city-relief-browser.mjs http://127.0.0.1:8790` ou visite `?debug&near=subida`.

Para conferir o indicador de entrada, o freio de mão pelo teclado e pelo toque, a marcha à ré e a colisão em uma curva, execute `node tests/driving-controls.mjs http://127.0.0.1:8790`.

## Pontos para revisão

Use `?debug&near=musica` para visitar a roda, experimentar os aplausos e lançar o drone com V. Acrescente `&weather=rain` ou `&weather=squall` para conferir as reações à chuva. O teste `node tests/street-performance.mjs http://127.0.0.1:8790` verifica mãos no violão, aplausos, objetos físicos, interrupções e pausa.

Execute `node tests/city-social-browser.mjs http://127.0.0.1:8790` para conferir observadores do drone, fotografias, conversa, guarda-chuvas e retorno às rotinas. `node tests/character-browser.mjs http://127.0.0.1:8790` verifica os modelos ao dirigir, pedalar e sentar. As juntas, contatos dos pés e limites de percepção também entram nos testes `tests/*.test.mjs`.

As cenas `?debug&near=vault-wall`, `vault-car` e `jump-car` começam de frente para a mureta, o capô e a traseira de um carro. Segure W e aperte Espaço. Em `pet` e `cat`, aperte E. Em `travessia`, segure F e solte para fechar o cruzamento. Esses mesmos objetos e controles estão disponíveis na exploração normal.

Use `?debug&near=dirigir` para começar junto à porta de um sedã na avenida da orla ou `?debug&near=farol` para conhecer a praia a pé.

Em `?debug&near=pedestres`, entre no sedã diante da faixa para observar a travessia ou buzinar.

Para repetir a aproximação à poltrona da Livraria Maré, abra `?debug&near=poltrona`. Outras cenas de revisão são `esquina-oeste` (faixa e calçada), `moradores` (encontro na praça), `telefone`, `camera`, `camera-hospital` e `fish-market`. Use o mesmo prefixo `?debug&near=` antes de cada nome.

Para inspecionar o estado no navegador, acrescente `?debug` à URL. Cenários úteis: `?debug&near=cafe`, `hospital`, `station`, `bike`, `pet`, `birds`, `mural`, `fountain`, `seat`, `car`, `jump-car`, `theft`, `push`, `curb`, `vehicle-hill`, `break`, `lamp-collision`, `cross`, `plaza-curve`, `signal`, `signal-break`, `ring`, `coast`, `curve`, `loop`, `hill`, `yield`, `turn`, `police`, `fire`, `police-info`, `fire-water`, `officer`, `firefighter`, `patrol`, `engine`, `driver`, `conversation`, `conversation-wall`, `boarding`, `exit-wall`, `home`, `restaurant`, `bus`, `bus-stop`, `bus-curve`, `pare`, `oneway`, `parking`, `avoid`, `avoid-blocked`, `pass`, `pass-blocked`, `pass-delay`, `pass-person`, `pass-solid`, `incident`, `impact-parked`, `impact-side`, `impact-glance`, `impact-wall`, `impact-solid-glance`, `impact-npc-side`, `impact-bus`, `routine-home` ou `routine-bus` (mantenha `near=` antes do nome). `&time=120` inicia à noite. Os dados de diagnóstico ficam no atributo `data-city` do elemento HTML e não aparecem durante a exploração normal; incluem distância dos pneus ao piso, estado do áudio, dos efeitos, das texturas e do ambient occlusion.
