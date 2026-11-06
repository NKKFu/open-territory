# Open Territory — Baía do Sol

Um jogo de exploração 3D em low poly, inspirado em Ilhas Mágicas. O jogo abre diretamente na cidade, sem menu, minimapa ou objetivos.

## Abrir o jogo

No macOS, dê dois cliques em `servir.command`. Ele abre o jogo no navegador e mantém um pequeno servidor local no Terminal. Para fechar o servidor, feche a janela do Terminal ou pressione Ctrl+C nela.

Também é possível iniciar manualmente, dentro desta pasta:

```sh
python3 -m http.server 8790 --bind 127.0.0.1
```

Depois acesse `http://127.0.0.1:8790/`. Se a porta estiver ocupada, use outra. O arquivo `index.html` precisa ser servido por HTTP por usar módulos JavaScript; o Three.js já está incluído em `vendor`, sem depender de download.

## Controles

- **WASD ou setas:** andar. **Shift:** correr. **Espaço:** pular.
- **Arrastar com o mouse:** olhar ao redor. **Roda do mouse:** aproximar ou afastar a câmera.
- **Portas:** caminhe contra as portas para empurrá-las. As do hospital e do posto são automáticas; os interiores ficam no mesmo mundo.
- **E:** conversar com um morador, acariciar um animal, sentar ou levantar de um banco livre, entrar ou sair de um carro ou bicicleta. A ação depende do objeto mais próximo.
- **Ao dirigir ou pedalar:** W/S aceleram ou dão ré, A/D viram, espaço freia e E sai. No carro, Q liga/desliga a seta esquerda e Z a direita. Ao pedir para sair em movimento, o veículo freia antes de liberar a saída quando houver espaço. A câmera acompanha o veículo; arraste para olhar ao redor.
- **R:** experimentar uma queda com ragdoll. O personagem levanta sozinho após alguns segundos.
- **Esc:** fechar uma conversa.

Em telas de toque, aparecem apenas os botões necessários para movimentação e interação. Arraste o cenário para controlar a câmera.

## O que há na cidade

Ruas conectadas à orla e às avenidas laterais, com calçadas, cruzamentos, faixas e semáforos; uma avenida curva e o Bairro Jardim; praças, lago, relevo suave, bosque, praia, píer e barcos. Há árvores, grama, flores, bancos, lixeiras, telefones públicos, caixas eletrônicos e pequenos detalhes espalhados pelo cenário.

O café, a galeria, a livraria, o mercado, a padaria, a floricultura, o hospital e a loja do posto têm interiores no próprio mapa. Moradores caminham, conversam ou descansam; há trabalhadores, cães, gatos e pombos. O jogo tem trânsito, carros e bicicletas utilizáveis, ciclo de dia e noite, faróis, iluminação urbana, partículas e alguns objetos que se quebram quando atingidos por um veículo.

A cidade é um mundo pequeno e delimitado visualmente pelo mar e pela floresta. O trânsito tenta parar para pedestres e segue rotas simples; a física é uma aproximação e não substitui uma simulação completa. Os barcos são parte do cenário e não são pilotáveis. O estado do mundo dura enquanto a página estiver aberta e reinicia ao atualizar.

## Pontos para revisão

Para inspecionar o estado no navegador, acrescente `?debug` à URL. Cenários úteis: `?debug&near=cafe`, `hospital`, `station`, `bike`, `pet`, `seat`, `car`, `break`, `cross`, `ring`, `coast`, `curve`, `loop`, `hill`, `yield` ou `turn` (mantenha `near=` antes do nome). `&time=120` inicia à noite. Os dados de diagnóstico ficam no atributo `data-city` do elemento HTML e não aparecem durante a exploração normal.
