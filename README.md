# Open Territory — Baía do Sol

Um jogo de exploração 3D em low poly, inspirado em Ilhas Mágicas. O jogo abre diretamente na cidade, sem menu, minimapa ou objetivos.

## Abrir o jogo

No macOS, dê dois cliques em `servir.command`. Ele abre o jogo no navegador e mantém um pequeno servidor local no Terminal. Para fechar o servidor, feche a janela do Terminal ou pressione Ctrl+C nela.

Também é possível iniciar manualmente, dentro desta pasta:

```sh
python3 -m http.server 8790 --bind 127.0.0.1
```

Depois acesse `http://127.0.0.1:8790/`. Se a porta estiver ocupada, use outra. O arquivo `index.html` precisa ser servido por HTTP por usar módulos JavaScript; o Three.js já está incluído em `vendor`, sem depender de download.

Quando o navegador usa renderização por software, o jogo reduz automaticamente a resolução interna e usa sombras de contato mais leves. Navegadores com GPU mantêm a resolução maior e as sombras dinâmicas. Isso não acrescenta menus nem controles à tela.

Os modelos fora da vista deixam de entrar nos lotes de desenho, preservando os que ainda projetam sombras visíveis. As rotinas continuam simuladas. Veículos parados reutilizam o apoio das rodas depois que a suspensão estabiliza e voltam a calculá-lo quando se movem.

O ambient occlusion acrescenta sombras suaves nos encontros de paredes, móveis, personagens e chão. Ele acompanha a câmera e os objetos em movimento, rejeita sombras falsas sobre superfícies planas e suaviza o resultado preservando as bordas. Navegadores sem os recursos WebGL necessários mantêm a iluminação normal.

A paleta tem vegetação, fachadas e roupas mais coloridas, com um ajuste leve de contraste e saturação. Uma vinheta suave escurece as bordas sem reduzir a visibilidade do centro. Asfalto, reboco, calçadas, madeira, grama, areia e água usam texturas geradas no próprio jogo em um atlas de 512 × 256 pixels. Os detalhes mantêm uma escala coerente entre objetos de tamanhos diferentes; os quarteirões continuam compartilhando os lotes de desenho.

Os sons começam no primeiro clique ou comando de teclado, respeitando o bloqueio de reprodução automática do navegador. Há vento, mar perto da praia, aves durante o dia, passos diferentes no piso e na grama, motores com volume e posição conforme a distância, portas e impactos. O áudio é gerado no próprio jogo, sem arquivos ou serviços externos. O volume também pode ser controlado ou silenciado pela aba do navegador.

## Controles

- **WASD ou setas:** andar. **Shift:** correr. **Espaço:** pular, inclusive sobre carros. Moradores em pé podem ser deslocados ao caminhar contra eles, quando houver espaço livre.
- **Arrastar com o mouse:** olhar ao redor. **Roda do mouse:** aproximar ou afastar a câmera.
- **Portas:** caminhe contra as portas para empurrá-las. As do hospital e do posto são automáticas; os interiores ficam no mesmo mundo.
- **E:** conversar com um morador, acariciar um animal, sentar ou levantar de um banco livre, entrar ou sair de um carro ou bicicleta. A ação depende do objeto mais próximo. Nas conversas, o morador para, olha para você e faz um gesto; aperte E por perto para ouvir outra fala. Ele retoma o passeio quando a conversa termina ou você se afasta. As falas aparecem na cena e a cidade continua em movimento. Paredes e portas fechadas bloqueiam interações.
- **Ônibus circular:** espere junto à porta na parada e pressione **E** para embarcar. O ônibus abre e fecha as portas, aguarda os passageiros e percorre quatro paradas. Durante a viagem, **E** solicita desembarque na próxima parada e exibe uma confirmação no mundo. No ponto, E desembarca assim que houver espaço livre; se a saída estiver ocupada, o ônibus aguarda. Sem solicitar saída, você continua a viagem.
- **Ao dirigir ou pedalar:** W acelera, S freia e engata a ré, A/D viram à esquerda/direita, espaço freia e E sai. No carro, Q liga/desliga a seta esquerda e Z a direita. Nas viaturas e no caminhão de bombeiros, L liga/desliga as luzes de emergência. Aproxime-se de uma porta lateral e pressione E para entrar em um carro parado; se houver motorista, ele sai primeiro quando há espaço. Ao pedir para sair em movimento, o veículo freia antes de liberar a saída pelo lado de entrada ou pelo lado oposto livre. A câmera acompanha o veículo; arraste para olhar ao redor.
- **R:** experimentar uma queda com ragdoll. O personagem levanta sozinho após alguns segundos.
- **Esc:** dispensar a fala visível.

Em telas de toque, aparecem apenas os botões necessários para movimentação e interação. Arraste o cenário para controlar a câmera.

## O que há na cidade

Ruas conectadas à orla e às avenidas laterais, com calçadas, cruzamentos, faixas e semáforos; uma curva na avenida junto à praça inicial, a avenida das Colinas e dois bairros com curvas e diagonais. As novas ruas de mão única têm duas faixas, setas de direção e placas de PARE que fazem o trânsito parar e dar preferência. Há estacionamentos, acessos pavimentados até as entradas, um terreno vazio, praças, lago, relevo suave, bosque, praia, píer e barcos. Árvores, grama, flores, bancos, lixeiras, telefones públicos e caixas eletrônicos completam o cenário.

O café, a galeria, a livraria, o mercado, a padaria, a floricultura, o hospital, a loja do posto, a delegacia e o quartel dos bombeiros têm interiores no próprio mapa, com luminárias que iluminam o ambiente à noite. Casas de moradores, térreos de prédios residenciais e dois restaurantes também podem ser visitados. Os restaurantes têm mesas, clientes e cozinha com fogão, panelas, pia e geladeira.

Os moradores alternam destinos e afazeres: casa, trabalho, comida, leitura, celular, conversas, brincadeiras, descanso e viagens de ônibus. Alguns adultos fumam ao ar livre. As vagas de cada atividade têm capacidade limitada; os moradores seguem calçadas e travessias, empurram portas e suspendem suas rotinas durante uma conversa, retomando-as depois. Nas paradas, quem desce tem prioridade; moradores que usam o circular retomam os afazeres depois de desembarcar. As falas acompanham o que estão fazendo. Alturas, roupas, cabelos, mochilas, chapéus e óculos variam. Há também policiais e bombeiros, trabalhadores, cães, gatos e pombos. Na delegacia, E consulta o quadro de informações; no quartel, E liga ou desliga o painel de treino com água.

Os pombos dão pequenos voos pela praça e reagem em grupos quando alguém corre perto. As asas acompanham o voo e fazem um ruído discreto; o pouso procura espaço livre e espera quando alguém ocupa o local. Os mesmos animais retomam seus passeios depois, sem aumentar a quantidade de modelos na cidade.

Há murais junto ao café, à galeria e a um dos residenciais. Dora pinta ao lado do Café Aurora, faz pausas e continua sua rotina; uma conversa interrompe o trabalho e ela volta a usar o pincel depois. A água tem movimento na superfície, a fonte produz pequenas ondas onde as gotas caem e algumas folhas flutuam com a brisa. Esses efeitos usam conjuntos fixos de instâncias, sem criar novos objetos a cada quadro.

O trânsito mantém distância de outros veículos, para antes das faixas nos sinais vermelhos e freia para pedestres. Os motoristas seguem as rotas usando o mesmo modelo de aceleração e direção do jogador, esterçando as rodas para fazer curvas e mudanças de faixa. Nas vias com duas faixas no mesmo sentido, podem mudar de faixa para contornar um veículo. Nos trechos retos de mão dupla, podem passar por um veículo parado ou objeto na pista quando há espaço, longe dos cruzamentos e sem tráfego ou pedestres na manobra; se necessário, recuam para ganhar espaço e esperam a faixa ficar livre. Nas batidas, os veículos recebem impulsos conforme a velocidade, o peso e o ponto de contato, podendo girar sem atravessar paredes ou pessoas. Após uma batida significativa com o jogador, o motorista para, liga o pisca-alerta, procura uma saída livre, sai para conferir e volta ao carro antes de retomar a rota. Faróis do jogador e do trânsito próximo iluminam o chão durante a noite. Há também bicicletas utilizáveis, ciclo de dia e noite e objetos, inclusive postes e semáforos, que se quebram em fragmentos e partículas ao serem atingidos por um veículo.

Cada roda acompanha o piso que está desenhado, incluindo rampas, meio-fio e relevo, com inclinação da carroceria e curso de suspensão. Os carros usam uma aproximação de motor, frenagem e aderência por roda: as rodas dianteiras viram, a direção fica menos brusca em alta velocidade, e grama oferece menos aderência que pavimento. A aceleração, as curvas e a frenagem também afetam a carroceria e o som do motor. O ciclista se apoia no selim, segura o guidão e acompanha os pedais. A câmera acompanha as transições pelas portas e procura uma vista por cima de coberturas baixas quando necessário.

A cidade é um mundo pequeno e delimitado visualmente pelo mar e pela floresta. A física e as rotas são simplificadas; em curvas e cruzamentos, os veículos aguardam quando não há espaço seguro para passar. Os andares superiores dos residenciais, os portões de garagem dos serviços e os barcos são cenográficos. Os prédios entráveis são acessíveis pelas portas de pedestres. O estado do mundo dura enquanto a página estiver aberta e reinicia ao atualizar.

## Verificação

Os testes de reserva de atividades, suspensão de rotinas, pintura e viagens, continuidade fora da câmera, impactos e manobras de veículos, reação dos motoristas a colisões, transformações e texturas dos modelos, apoio das rodas em pisos, meios-fios e encostas e voos dos pombos usam o executor nativo do Node.js:

```sh
node --test tests/*.test.mjs
```

As interações e a navegação do mapa também precisam ser verificadas no navegador.

## Pontos para revisão

Para inspecionar o estado no navegador, acrescente `?debug` à URL. Cenários úteis: `?debug&near=cafe`, `hospital`, `station`, `bike`, `pet`, `birds`, `mural`, `fountain`, `seat`, `car`, `jump-car`, `theft`, `push`, `curb`, `vehicle-hill`, `break`, `lamp-collision`, `cross`, `plaza-curve`, `signal`, `signal-break`, `ring`, `coast`, `curve`, `loop`, `hill`, `yield`, `turn`, `police`, `fire`, `police-info`, `fire-water`, `officer`, `firefighter`, `patrol`, `engine`, `driver`, `conversation`, `conversation-wall`, `boarding`, `exit-wall`, `home`, `restaurant`, `bus`, `bus-stop`, `bus-curve`, `pare`, `oneway`, `parking`, `avoid`, `avoid-blocked`, `pass`, `pass-blocked`, `pass-delay`, `pass-person`, `pass-solid`, `incident`, `impact-parked`, `impact-side`, `impact-glance`, `impact-wall`, `impact-solid-glance`, `impact-npc-side`, `impact-bus`, `routine-home` ou `routine-bus` (mantenha `near=` antes do nome). `&time=120` inicia à noite. Os dados de diagnóstico ficam no atributo `data-city` do elemento HTML e não aparecem durante a exploração normal; incluem distância dos pneus ao piso, estado do áudio, dos efeitos, das texturas e do ambient occlusion.
