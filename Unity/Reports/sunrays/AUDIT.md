# Raios solares

Efeito de imagem nativo do pipeline Built-in, ligado ao sol existente. A máscara usa a profundidade dos objetos opacos e um passe explícito das mesmas malhas das nuvens. Dois passes de desfoque radial formam os feixes; a composição preserva os destaques da imagem. A cor acompanha a altura do sol, aguaceiros reduzem a intensidade e o efeito fica ausente à noite ou com o sol atrás da câmera.

`Esc → Imagem → Raios solares` liga/desliga o efeito nas qualidades Equilibrada e Alta. Leve não o executa. O lado maior dos buffers é limitado a 480 ou 640 pixels, respectivamente. O efeito e a oclusão ambiente compartilham o pedido da textura de profundidade e liberam os recursos ao serem desligados. Não há câmera adicional, geometria de raios, colisores ou trabalho de QA em partidas normais.

## Validação final

- Unity 6000.3.27f1; pipeline Built-in; Windows x64; Direct3D12.
- Build final: `build-23a3938f38`, concluído com sucesso, sem erros.
- `editor/sunrays-qa.json`: 26 verificações aprovadas.
- `native-default/sunrays-qa.json`: 26 verificações aprovadas, sem erros de execução.
- Player: `Unity/OpenTerritory/Builds/WindowsRelease/BaiaDoSol.exe`.
- SHA-256 do assembly: `838CA0C98391919F9C48B1D777504503185492339BE0554A412C44D34455A03C`.
- Processo nativo encerrado com código 0; assembly permaneceu igual durante o teste.
- Metadados completos: `native-default/verification.json`, gerado em `2026-11-08T03:57:45Z`.

Foram comparadas renderizações reais com o efeito desligado/ligado entre pilares, diante de uma parede fechada e sob uma nuvem. A parede e a nuvem impediram completamente o efeito: zero pixels alterados. Também foram verificados MSAA 0/2/4, ausência de feixes noturnos e com o sol atrás da câmera, intensidade reduzida no aguaceiro, controles reais do uGUI, limites de qualidade, independência em relação à oclusão ambiente, separação do novo controle e do rodapé, pausa e retomada.

As capturas incluem `01-pillars-on.png`, `03-covered-view-on.png`, `04-cloud-occlusion-on.png`, `07-city-on.png` e `08-settings.png`, em `native-default`. A imagem de configurações usa os mesmos controles uGUI temporariamente renderizados pela câmera de QA, pois `Camera.Render` não inclui Canvas em modo Overlay. Esse modo é restaurado após a captura.

Reprodução: execute `Unity/Verificar-Raios-Solares.ps1`. O probe é instalado somente com `--city-sunrays-qa` ou por anexação explícita em Play Mode; a prova preserva as opções anteriores e não grava PlayerPrefs. O Editor foi deixado fora de Play Mode com a cena sem alterações não salvas. Não foi feita comparação de FPS nem se afirma ganho de desempenho.
