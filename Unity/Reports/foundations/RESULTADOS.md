# Fundações dos prédios — 8 de novembro de 2026

As bases dos 217 prédios agora se ligam ao terreno com geometria visível e colisão sólida. Os pisos permanecem nivelados nas alturas existentes. As fundações seguem a rotação e a planta de cada prédio, com um recuo de 8 mm para evitar faces sobrepostas à laje, e terminam pelo menos 20 cm abaixo dos vértices do relevo que abrangem sua planta. O topo permanece abaixo do piso acabado. Portas, interiores, calçadas, muros, jardins e mobiliário conservam suas posições.

`Assets/Editor/CityFoundationBuilder.cs` atualiza somente as fundações pelo menu **Open Territory → Assentar bases dos prédios no terreno**. O importador completo também chama esse construtor. As 217 bases reutilizam uma malha e um material, preparados no Editor e salvos em `Assets/Imported/CityFoundations.asset`; não há geração de bases nem probe novo durante uma partida normal. A cena anterior à correção está em `backups/Before-foundations-20261108-061518.unity`.

## Verificação específica da correção

- `editor-validation.json`: aprovado nos 217 prédios, em 59.076 amostras de perímetro, com espaçamento máximo de 25 cm. Nenhuma fundação ausente, amostra sem fechamento, falha de colisão ou divergência de planta/altura.
- Os dados do relevo indicavam vãos maiores que 3 cm em 215 plantas, chegando a aproximadamente 4,20 m nas amostras. Os pisos físicos reaproveitados escondiam parte desses desníveis dos raycasts de chão; a validação também considera os triângulos do relevo visível.
- Capturas reais do Unity nos prédios 13 e 135, com as mesmas câmeras antes/depois: `../../OpenTerritory/Captures/Foundations/before-13.png`, `after-13.png`, `before-135.png` e `after-135.png`. Os pontos de vista estão em `review-views.json`. Os vãos visíveis foram fechados.
- Verificação final no Editor: fora do Play Mode, raiz das fundações ativa, 217 bases e validação específica aprovada.

## Player nativo e acessos

Build completo exclusivo: `Unity/OpenTerritory/Builds/FoundationsValidated-20261108/BaiaDoSol.exe`, Windows x64, release, sem `BuildScriptsOnly`. O log do MCP registra build bem-sucedido em 108 segundos; `build.txt` conserva esse registro. `frozen-player.json` registra os hashes dos 149 arquivos e confirma as 217 bases serializadas em `BaiaDoSol_Data/level0`, cobrindo os índices 0–216.

O teste opt-in `--city-frontage-qa` rodou nesse executável, com API padrão **Direct3D 12**, Radeon RX 9070 e resolução solicitada de 1280 × 800. A sessão usa armazenamento isolado de QA. Resultado: **18 de 19 verificações aprovadas; saída 2**, não uma aprovação integral da suíte.

- As seis caminhadas da calçada ao interior e as seis aberturas de portas passaram.
- As 1.739 amostras de piso de acesso tiveram suporte físico contínuo; todas as entradas mantiveram suas conexões de navegação. Nenhum erro de execução do jogo foi registrado.
- A verificação geral de obstáculos encontrou **24 amostras bloqueadas em sete acessos**, por colisores de mobiliário/muros e hastes existentes. `access-baseline.txt` repete a auditoria na mesma cena com as fundações temporariamente desativadas e depois ativadas: **24 antes, 24 depois, mesmas amostras e mesmos obstáculos**. O estado ativo foi restaurado. Não foram feitas alterações nesses objetos durante esta correção.

Relatórios completos: `native-default/frontage-qa.json`, `native-default/frontage-qa.log` e `native-default/verification.json`. A verificação registra o caminho do executável, data, código de saída, API, hashes do assembly e da cena serializada do player. O hash da cena fonte é identificado como estado observado na validação, pois outros trabalhos continuaram salvando a cena compartilhada depois deste build.
