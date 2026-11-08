# Destinos do mapa interativo

## Causa

`CityMapUI.AddPlaces` aceitava somente três tipos de imóvel: hospital, mercado e posto. A armaria existia no manifesto e tinha compra, munição e recompra em `CityCommerce`, mas seu tipo `armory` não entrava na lista do mapa. O mesmo filtro excluía outros estabelecimentos públicos. Os serviços de rua também não eram cadastrados.

O guia tinha oito destinos e quatro paradas. A composição vertical era fixa, sem rolagem, e desaparecia em telas estreitas. Acrescentar os nomes à lista antiga faria os botões se sobreporem aos controles inferiores.

## Cobertura corrigida

O mapa agora contém **27 destinos e quatro paradas**, obtidos das posições do manifesto ativo. Os 13 estabelecimentos públicos nomeados estão representados. Foram incluídos estes 19 destinos:

| Destino | Informação no guia |
| --- | --- |
| Oficina Farol | Armaria · armas e munição |
| Flor do Mar | Floricultura |
| Padaria Pão & Sol | Padaria |
| Galeria Sol | Galeria de arte |
| Livraria Maré | Livraria |
| Café Aurora | Café |
| Cozinha da Praça | Restaurante |
| Cantina do Bairro | Restaurante |
| Delegacia | Delegacia |
| Bombeiros | Bombeiros |
| Caixa · Café Aurora | Caixa eletrônico |
| Caixa · Mercado da Praça | Caixa eletrônico |
| Caixa · Hospital da Baía | Caixa eletrônico |
| Estande de tiro | Prática de tiro |
| Roda do Ipê | Música ao vivo |
| Guindaste do porto | Guindaste |
| Estação de drones | Drones |
| Posto meteorológico | Previsão do tempo |
| Guia da cidade | Mapa da cidade |

Fonte da praça, Galpão da Maré, cinema, porto, hospital, mercado, posto e marina já constavam. A marina usava a posição da parada de ônibus: agora marca o píer físico das embarcações Brisa e Estrela; `Parada · Marina` conserva sua posição própria.

Os dois painéis de guia próximos entre si compartilham um destino. As vitrines de compra/recompra pertencem à Oficina Farol; atendimento médico, informações policiais, treinamento dos bombeiros e rádio pertencem aos respectivos destinos. Os 204 imóveis residenciais continuam com suas plantas na carta. A cobertura de destinos não inclui objetos móveis, câmeras e demais dispositivos de hacking.

## Uso e apresentação

Os imóveis públicos nomeados entram automaticamente, sem uma lista fechada de tipos comerciais. O guia é alfabético, com tipo de local, rolagem por roda/arraste e barra de rolagem. Filtro de paradas, seleção, distância e remoção ficam fora da área rolável.

O botão `Locais` abre o mesmo guia em telas estreitas; escolher um destino retorna à carta. O nome selecionado tem prioridade sobre os demais. Rótulos evitam outros nomes e os controles do mapa. As instruções do rodapé se ajustam à largura.

## Verificação

Suíte opt-in: `Unity/Verificar-Mapa-Interativo.ps1 -ReportDirectory Unity/Reports/interactive-map/catalog-audit`.

O relatório `catalog-audit/interactive-map-qa.json` registra os resultados, e `catalog-audit/verification.json` identifica o assembly do executável testado por SHA-256. As verificações cobrem os 13 estabelecimentos, serviços públicos, botões para todos os destinos, localização física da marina, alcance do último item, roda sem zoom da carta, arraste por toque, seleção real da armaria pelo uGUI, prioridade de seu rótulo, controles do jogo, simulação contínua, pausa por Esc e proporções de tela 1280×800, 1920×1080, 1024×768, 800×800 e 540×960.

Capturas da armaria: `catalog-audit/07-armaria-no-mapa.png` e `catalog-audit/09-armaria-tela-compacta.png`. Capturas do guia aberto em telas estreitas: `catalog-audit/11-guia-tela-compacta.png` e `catalog-audit/12-guia-vertical.png`. O probe permanece ausente em uma partida normal.
