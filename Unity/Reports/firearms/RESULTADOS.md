# Armas, empunhadura e ferimentos — 8 de novembro de 2026

Entrega completa e isolada: `Unity/OpenTerritory/Builds/Firearms-20261108-0800/BaiaDoSol.exe`. `Unity/Jogar-Armas.ps1` abre essa cópia. O executável compartilhado `WindowsRelease` não é a entrega desta revisão.

Pistola e carabina foram remodeladas; a carabina tem acabamento coyote e coronha curta. As mãos fechadas usam encaixes próprios no cabo e no apoio, com poses de descanso, mira, elevação, movimento lateral, recuo e recarga. Os disparos e mecanismos foram remixados a partir do cache de gravações CC0, com atenuação espacial e reflexão em interiores.

Pistola e carabina causam 36 e 25 de dano, respectivamente, com sangue no ponto de acerto e manchas temporárias no piso. O pulsor causa 12 de dano e atordoamento elétrico. Policiais podem responder a um suspeito armado identificado, com reação inicial, cadência, recarga e verificação física até o alvo. Cobertura bloqueia os tiros; guardar a arma interrompe a resposta armada. O sistema de sangue tem 256 partículas e 32 manchas reutilizáveis, sem crescimento durante os impactos, com expiração pelo relógio da cidade e pausa por Esc.

## Validação da entrega

Todas as execuções abaixo usaram o mesmo assembly `D0C9B772B9102EA02A47A01BD124F83EF1101F3B07E64ADF8B178BBC8992A462`, a API padrão Direct3D 12 e encerraram com código **0**. Foram **166 verificações aprovadas**:

| Execução | Verificações | Evidência |
| --- | ---: | --- |
| Ferimentos e empunhadura | 67 | `native/firearm-injury-qa.json` |
| Armas, disparos, efeitos, recargas e áudio no listener | 63 | `weapons-native/weapons-qa.json` |
| Movimento, mira, câmera, cobertura e pausa | 32 | `locomotion-native/qa.json` |
| Inicialização, caminhada e pausa na cidade normal | 4 | `normal-native/verification.json` |

As três suítes principais rodaram sequencialmente em 1280×800, com armazenamento de QA isolado. A inicialização normal usou 1280×720 e o controle opt-in `--city-debug`, sem instalar os probes de aceitação. Não houve erro de execução nessas quatro validações. O áudio capturado em `weapons-native/weapon-mix.wav` tem pico 0,562 e 858.112 quadros de áudio. As capturas dos modelos, mãos, disparos, recargas, ferimentos e sangue ficam em `native` e `weapons-native`.

`build-result.json` registra o build completo; `verification.json` consolida os testes. `frozen-player.json` congela todos os 142 arquivos da entrega, incluindo dados comprimidos da cena, blobs de áudio/texturas, metadados e engine. A cena não foi reconstruída por esta tarefa. O Editor foi auditado com a cena limpa, fora de Play Mode, assembly posterior aos scripts e os novos membros carregados por reflexão.

## Evidências auxiliares e limites

As tentativas extras de usar `ScreenCapture.CaptureScreenshot` na janela oculta da cidade falharam, tanto com execução comum quanto em batch. Os registros estão em `city-native` e `city-native-batch`; essas tentativas **não** compõem os resultados aprovados. As capturas verificadas das suítes usam `Camera.Render` e RenderTexture. Uma execução posterior da cidade sem solicitar essa API de captura passou com zero erros e saída normal (`normal-native`).

A primeira rodada de ferimentos não retomava a entrada de mira após Esc liberar os controles; o probe foi corrigido antes da validação final. Uma rodada inicial da suíte de armas também encontrou o pulso já expirado depois de uma captura demorada; os registros originais foram preservados em `initial` e `weapons-initial`. A rodada final do player entregue passou todas as verificações, inclusive simulação e renderização das partículas de pulso.

Os testes verificam comportamento e aparência; não medem ganho de FPS nem desempenho em hardware diferente. Outros trabalhos continuaram usando o Editor e builds próprios; nenhuma sessão de Play Mode iniciada por outro trabalho foi encerrada por esta revisão.
