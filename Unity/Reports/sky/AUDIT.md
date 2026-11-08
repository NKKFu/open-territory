# Sol e lua — validação

Implementação em `Assets/Scripts/World/CitySky.cs`, integrada a `CitySystems`, com o shader `Assets/Resources/Shaders/CitySky.shader`. O céu usa o ciclo existente de 300 segundos, com órbita de 42 graus, discos facetados e lua com crateras. A fonte direcional acompanha o astro acima do horizonte. Nenhum probe ou colisor é acrescentado à partida normal.

## Play Mode — Unity 6000.3.27f1

`sky-qa.json`: **21 verificações aprovadas**. O script de reprodução, para execução explícita pelo MCP com a cena pronta em Play Mode, está em `verify-editor.cs.txt`. Não pertence a Assets e não é executado em uma partida normal.

As verificações renderizam imagens reais e comparam pares com/sem os discos. Cobrem nascer do sol, dia, pôr do sol, lua, enquadramento com a câmera atual, alinhamento das sombras, ausência de imagem espelhada do sol, ausência de paralaxe, proteção contra a distância curta da neblina de rua, redução de brilho no aguaceiro e oclusão por geometria e pela malha real das nuvens.

Capturas revisadas: `01-sunrise.png`, `02-day.png`, `03-sunset.png` e `04-moon.png`.

## Executável Windows

`native/native-qa.json`: pausa e retomada aprovadas; o relógio permaneceu em 10,798164 segundos durante a pausa e voltou a avançar após continuar. O controlador real deslocou o personagem em 0,98 m pelo caminho de entrada compartilhado com os controles de toque. A sessão usou `--city-debug`, que isola o armazenamento de QA, e foi encerrada por seu comando de saída.

O teste de captura via `ScreenCapture.CaptureScreenshot` no player oculto em batch mode falhou e produziu a única entrada de erro dessa sessão. As evidências visuais são as capturas e comparações de Play Mode, feitas por `Camera.Render`; não se atribui essa captura falha ao shader ou à simulação.

O build Windows atualizado inclui os scripts e o shader em Resources. A compilação do shader foi conferida sem erros. Havia outros trabalhos no mesmo projeto durante a validação; arquivos e configurações alheios a este recurso foram preservados.
