# Rotas e estilhaços — validação nativa

As rotas usam uma fila de prioridade com desempate estável, buffers por instância e gerações de busca. A reconstrução do caminho usa inserção no fim e inversão, e a seleção dos nós reutiliza a lista e o comparador. Os estilhaços são preparados durante a inicialização: há 72 corpos reutilizáveis, com reciclagem do mais antigo quando o limite é atingido. A devolução retira a participação física e limpa o movimento e a aparência; cada emissão recebe novo prazo no relógio da cidade. A iluminação também é localizada uma vez por serviço, e as cores dos vértices são lidas uma vez por quebra.

## Comparação aceita

Três processos sequenciais por variante, Unity 6000.3.27f1, player Windows release/Mono, Direct3D 11, 1280 × 720, qualidade 1, MSAA 2, VSync retirado somente no probe. Máquina: Ryzen 9 7900X e Radeon RX 9070. As janelas de QA ficam ocultas. Cada rodada mede 144 buscas em 48 pares reais de nós e 64 destruições por seus colisores, além do cenário automatizado da cidade.

As variantes Before/After partem do mesmo build completo. O define exclusivo de build `CITY_PERFORMANCE_REFERENCE` seleciona os algoritmos históricos; ele não existe na entrega normal nem nos defines persistidos. Cena, blobs de áudio/textura, metadados, dependências e engine foram conferidos por hash. As 48 assinaturas de trajetos coincidiram em todas as rodadas, inclusive o caso sem caminho. As alterações simultâneas de outros trabalhos permanecem no conteúdo comum.

Valores abaixo: mediana das médias de cada uma das três rodadas.

| Operação | Referência | Otimizada | Redução de tempo |
|---|---:|---:|---:|
| Cálculo de rota | 7,23 ms | 1,68 ms | 76,7% |
| Destruição de objeto | 5,19 ms | 0,69 ms | 86,7% |

O p95 das rotas passou de 17,74 para 3,51 ms; o da destruição, de 6,24 para 0,93 ms. O cenário mediu aproximadamente 2,44 MiB adicionais de memória usada com os buffers e o pool preparados. Esses resultados por operação não são uma promessa de aumento equivalente no FPS da partida. As medições de GPU e de alocação gerenciada não estavam disponíveis nessa configuração; o contador por thread falhou na calibração e foi descartado. O contador de desenhos retornou somente zeros e também não é usado na comparação.

Resultados brutos aceitos: `before/run-1..3` e `after/run-1..3`. A comparação consolidada é `comparison.json`. As pastas `baseline`/`optimized` guardam tentativas anteriores e não são usadas na conclusão.

## Executável entregue

`Unity/OpenTerritory/Builds/WindowsOptimized/BaiaDoSol.exe` é a cópia isolada do build completo com os algoritmos otimizados. Ela evita que compilações simultâneas em `WindowsRelease` substituam o executável validado. O hash do assembly permaneceu `A2FD93FD3EE260A6E58D29AF2564CA2B66A9D65DF5F743739232EDBDD61B2219` nas três validações abaixo. Foram aprovados:

- 14 verificações da otimização no executável entregue, incluindo rotas reais, falha/recuperação de busca, dano, limite de 72 corpos, cor, filtros de colisão, pausa, expiração e devolução ao pool;
- 26 verificações de circulação, incluindo moradores em movimento, passagem por portas e segurança do trânsito;
- 4 verificações numa sessão normal com a API gráfica padrão: caminhada pelo poller real, pausa do relógio, ausência de erros de jogo e encerramento com código zero;
- 71 verificações gerais do mapa na cópia isolada, todas aprovadas;
- todas as seis rodadas A/B, com encerramento normal e sem erros de jogo.

As quatro suítes da entrega somam 115 verificações aprovadas. Os metadados de cada execução identificam o mesmo assembly testado. Os relatórios da cópia isolada estão em `delivered/run-1`, `delivered-circulation`, `delivered-default` e `delivered-general`; `delivered-build.json` identifica a origem e o conteúdo copiado. Os relatórios `release`, `circulation` e `default-player` preservam as validações anteriores do executável compartilhado.

## Histórico das falhas gerais

Um build compartilhado anterior executou 68 verificações e apresentou as mesmas seis falhas tanto no WindowsRelease quanto na variante com os algoritmos anteriores:

- cobertura de prédios/cenário por LOD;
- validade/redução das malhas de LOD;
- redução de janelas;
- redução da iluminação de rua;
- entrada no veículo selecionado pelo teste;
- quantidade de ações de comércio/mundo esperada pelo teste.

Essas invariantes e interações não foram modificadas para aprovar o teste. A reprodução e a lista exata ficam em `general-regression.json`, `before-reference` e `release-reference`. Após a integração dos demais trabalhos simultâneos, a cópia isolada foi novamente validada: as 71 verificações gerais passaram. As falhas acima são históricas e não se reproduzem na entrega `WindowsOptimized`.

As primeiras sessões instrumentadas em Direct3D 12 falharam ao encerrar em `D3D12Core.dll`; elas foram rejeitadas. A comparação aceita usa Direct3D 11 nas duas variantes. A sessão normal final, sem o probe de desempenho, passou também com a API padrão do projeto. Não foi alterada a configuração gráfica da partida.
