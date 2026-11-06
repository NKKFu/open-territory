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

## Controles

- **WASD ou setas:** andar. **Shift:** correr. **Espaço:** pular.
- **Arrastar com o mouse:** olhar ao redor. **Roda do mouse:** aproximar ou afastar a câmera.
- **Portas:** caminhe contra as portas para empurrá-las. As do hospital e do posto são automáticas; os interiores ficam no mesmo mundo.
- **E:** conversar com um morador, acariciar um animal, sentar ou levantar de um banco livre, entrar ou sair de um carro ou bicicleta. A ação depende do objeto mais próximo. Nas conversas, o morador para, olha para você e faz um gesto; aperte E por perto para ouvir outra fala. Ele retoma o passeio quando a conversa termina ou você se afasta. As falas aparecem na cena e a cidade continua em movimento. Paredes e portas fechadas bloqueiam interações.
- **Ônibus circular:** espere junto à porta na parada e pressione **E** para embarcar. O ônibus abre e fecha as portas, aguarda os passageiros e percorre quatro paradas. Durante a viagem, **E** solicita desembarque na próxima parada; enquanto estiver parado, E desembarca imediatamente se a calçada estiver livre. Sem solicitar saída, você continua a viagem.
- **Ao dirigir ou pedalar:** W/S aceleram ou dão ré, A/D viram, espaço freia e E sai. No carro, Q liga/desliga a seta esquerda e Z a direita. Nas viaturas e no caminhão de bombeiros, L liga/desliga as luzes de emergência. Só veículos estacionados e sem motorista estão disponíveis para embarque. Ao pedir para sair em movimento, o veículo freia antes de liberar a saída pelo lado de entrada ou pelo lado oposto livre, sem atravessar paredes, pessoas ou outros carros. A câmera acompanha o veículo; arraste para olhar ao redor.
- **R:** experimentar uma queda com ragdoll. O personagem levanta sozinho após alguns segundos.
- **Esc:** dispensar a fala visível.

Em telas de toque, aparecem apenas os botões necessários para movimentação e interação. Arraste o cenário para controlar a câmera.

## O que há na cidade

Ruas conectadas à orla e às avenidas laterais, com calçadas, cruzamentos, faixas e semáforos; uma curva na avenida junto à praça inicial, a avenida das Colinas e dois bairros com curvas e diagonais. As novas ruas de mão única têm duas faixas, setas de direção e placas de PARE que fazem o trânsito parar e dar preferência. Há estacionamentos, acessos pavimentados até as entradas, um terreno vazio, praças, lago, relevo suave, bosque, praia, píer e barcos. Árvores, grama, flores, bancos, lixeiras, telefones públicos e caixas eletrônicos completam o cenário.

O café, a galeria, a livraria, o mercado, a padaria, a floricultura, o hospital, a loja do posto, a delegacia e o quartel dos bombeiros têm interiores no próprio mapa, com luminárias que iluminam o ambiente à noite. Casas de moradores, térreos de prédios residenciais e dois restaurantes também podem ser visitados. Os restaurantes têm mesas, clientes e cozinha com fogão, panelas, pia e geladeira.

Os moradores alternam destinos e afazeres: casa, trabalho, comida, leitura, celular, conversas, brincadeiras, descanso e viagens de ônibus. Alguns adultos fumam ao ar livre. As vagas de cada atividade têm capacidade limitada; os moradores seguem calçadas e travessias, empurram portas e suspendem suas rotinas durante uma conversa, retomando-as depois. Nas paradas, quem desce tem prioridade; moradores que usam o circular retomam os afazeres depois de desembarcar. As falas acompanham o que estão fazendo. Alturas, roupas, cabelos, mochilas, chapéus e óculos variam. Há também policiais e bombeiros, trabalhadores, cães, gatos e pombos. Na delegacia, E consulta o quadro de informações; no quartel, E liga ou desliga o painel de treino com água.

O trânsito mantém distância de outros veículos e freia para pedestres. Nas vias com duas faixas no mesmo sentido, pode mudar de faixa para contornar um veículo, quando a faixa vizinha está livre e longe do PARE. Após uma batida significativa com o jogador, o motorista para, liga o pisca-alerta, procura uma saída livre, sai para conferir e volta ao carro antes de retomar a rota. Faróis do jogador e do trânsito próximo iluminam o chão durante a noite. Há também bicicletas utilizáveis, ciclo de dia e noite, partículas e objetos que se quebram quando atingidos por um veículo.

A cidade é um mundo pequeno e delimitado visualmente pelo mar e pela floresta. A física e as rotas são simplificadas; carros não invadem a contramão para ultrapassar. Os andares superiores dos residenciais, os portões de garagem dos serviços e os barcos são cenográficos. Os prédios entráveis são acessíveis pelas portas de pedestres. O estado do mundo dura enquanto a página estiver aberta e reinicia ao atualizar.

## Verificação

Os testes de reserva de atividades, suspensão de rotinas e viagens, continuidade fora da câmera, reação dos motoristas a colisões e transformações dos modelos renderizados usam o executor nativo do Node.js:

```sh
node --test tests/*.test.mjs
```

As interações e a navegação do mapa também precisam ser verificadas no navegador.

## Pontos para revisão

Para inspecionar o estado no navegador, acrescente `?debug` à URL. Cenários úteis: `?debug&near=cafe`, `hospital`, `station`, `bike`, `pet`, `seat`, `car`, `break`, `cross`, `ring`, `coast`, `curve`, `loop`, `hill`, `yield`, `turn`, `police`, `fire`, `police-info`, `fire-water`, `officer`, `firefighter`, `patrol`, `engine`, `driver`, `conversation`, `conversation-wall`, `boarding`, `exit-wall`, `home`, `restaurant`, `bus`, `bus-stop`, `pare`, `oneway`, `parking`, `avoid`, `avoid-blocked`, `incident`, `routine-home` ou `routine-bus` (mantenha `near=` antes do nome). `&time=120` inicia à noite. Os dados de diagnóstico ficam no atributo `data-city` do elemento HTML e não aparecem durante a exploração normal.
