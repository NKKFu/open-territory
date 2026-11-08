Reações a agressões e assaltos implementadas e validadas em 8 de novembro de 2026, no Unity 6000.3.27f1, Windows x64.

Vítimas reagem ao confronto, testemunhas fogem ou pedem ajuda e a polícia responde ao agressor identificado. Ocorrências de NPCs conservam suspeitos próprios e não incriminam o jogador que apenas presencia a cena. A vítima pode se defender antes de ligar e retoma a chamada quando fica fora do alcance do agressor. Um soco defensivo imediato do jogador contra um NPC que iniciou a agressão preserva a responsabilidade do autor original.

Adultos em circulação podem iniciar agressões ou assaltos ocasionais. O assaltante se volta para a vítima, faz um gesto de intimidação e foge depois da ação. O jogador pode usar **E → Rob** ao intimidar uma pessoa de perto, enquanto ela mantém as mãos levantadas. A vítima entrega o dinheiro que carrega e a ocorrência provoca reações e atendimento policial. A polícia persegue, procura ao perder contato e detém o NPC; o jogador mantém **E → Surrender** durante a abordagem.

As quatro suítes passaram no mesmo executável, com a API gráfica padrão do projeto e código de saída **0** em todos os processos:

| Suíte nativa | Verificações |
| --- | ---: |
| Crimes: agressões, assaltos, suspeitos, defesa, chamadas, busca, prisão, pausa e reforços no mapa atual | 54 |
| Corpo a corpo: contatos, combos, cobertura, contra-ataques, guarda e toque | 52 |
| Reações: conversas, armas, percepção, testemunhas, memória e pausa | 33 |
| Armas: controles, disparos, cobertura, recarga, efeitos e áudio nativo | 63 |
| **Total** | **202** |

A inicialização normal também foi conferida no Editor: cidade pronta, 24 moradores ativos, nenhuma ocorrência e nenhum componente de QA instalado. Não houve erro no Console; o Play Mode foi encerrado e a cena permaneceu salva, sem alterações pendentes. Os controles de toque foram exercitados pelos componentes reais da interface; não houve validação em uma tela sensível ao toque física.

Executável: `C:/Users/nelso/open-territory/Unity/OpenTerritory/Builds/CrimeResponse/BaiaDoSol.exe`.

Assembly SHA-256: `D427991286B9AF9EE06CAD35DC244F93FE1450A9CE058D37DCF5B905BC24D60A`.

O build foi completo, com limpeza do cache e sem `BuildScriptsOnly`. `build-final.json` registra a construção; `validation.json` reúne as aprovações e os hashes. `player-manifest.json` congela os 149 arquivos entregues, incluindo cena, assemblies, áudio e texturas. `source-files.json` registra os arquivos da implementação. Os relatórios detalhados e as capturas ficam em `native` e `regressions`; `normal-startup.json` documenta a inicialização normal e o encerramento do Play Mode.
