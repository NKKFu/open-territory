# Parque linear Bay Gardens

A faixa aprovada corresponde aos imóveis 56, 48, 36, 27 e 23 do manifesto. Os imóveis 61 e 19, nas extremidades, e as calçadas existentes foram preservados. Os quintais associados no exportador são `yard-27`, `yard-36` e `yard-48`.

O construtor `CityLinearParkBuilder` guarda uma cópia da cena antes da alteração e mantém referências aos objetos, colisões e malhas originais. Para desfazer no Editor, abra `BaiaDoSol` salva e use **Open Territory > Restaurar casas do parque linear**. Para aplicar novamente, use **Open Territory > Construir parque linear na orla**. Esses comandos não apagam os prefabs da cidade.

As fachadas, interiores, fundações, painéis e objetos exclusivos são desativados; os triângulos dos três quintais e das peças misturadas em malhas compartilhadas são removidos em cópias dessas malhas. O arquivo `LinearParkYards.json` registra a geometria identificada no exportador e nas entradas da cena. O construtor interrompe a operação se a geometria esperada não coincidir, em vez de modificar lotes vizinhos.

O parque usa os materiais e árvores existentes, geometrias agrupadas por material, superfícies físicas e ligações bidirecionais ao grafo de pedestres da cidade. O `CityBuilder` aplica o parque após a geração da cidade; a reserva no gerador de vegetação mantém os novos passeios desobstruídos.

`placement.json` registra a auditoria das construções e colisões preservadas. Os testes nativos são opt-in, usam armazenamento isolado e são executados com `Unity/Verificar-ParqueLinear.ps1` apontando para um player e uma pasta de relatório exclusivos.

Em 2026-11-08, o player completo `Unity/Deliveries/LinearPark-20261108-Final2/BaiaDoSol.exe` foi validado em Direct3D 12: parque 22/22, orla 23/23 e circulação 26/26, todos com saída zero e assembly SHA-256 `337B360BD2B925A5652D3CEC5A734F07CDDB90DC8E68512E4DFD6F839E562BEA`. Os relatórios estão nas pastas `native-final2-park`, `native-final2-waterfront` e `native-final2-circulation`; as capturas do parque estão na primeira delas.
