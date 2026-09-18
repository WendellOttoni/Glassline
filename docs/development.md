# Desenvolvimento do Glassline

## Organização da solução

As dependências apontam para dentro:

```text
Glassline.App ───────────────┐
        │                    ▼
        └─> Infrastructure -> Core
```

- `Glassline.Core` não conhece WinUI, Windows App SDK, SMTC nem named pipes.
  Ele contém o estado normalizado, os comandos e os contratos dos providers.
- `Glassline.Infrastructure` implementará a integração com o Windows e com o
  Noctune. Ele depende somente do Core.
- `Glassline.App` faz a composição e apresenta o estado. A UI não deve conter
  regras de protocolo ou acesso direto ao SMTC.

## Comandos locais

```powershell
dotnet restore Glassline.slnx
dotnet build Glassline.slnx -c Debug
dotnet test Glassline.slnx -c Debug
dotnet run --project src/Glassline.App/Glassline.App.csproj
```

O primeiro `restore` requer acesso ao NuGet. O `dotnet run` registra uma
identidade MSIX temporária para desenvolvimento e requer o modo de
desenvolvedor do Windows.

## Interação do protótipo

- A cápsula abre centralizada no topo do monitor principal e não aparece na
  barra de tarefas nem no alternador `Alt+Tab`.
- Ao manter o ponteiro sobre ela por 110 ms, o painel expande. Ao sair, ele
  recolhe após 240 ms para evitar oscilações nas bordas.
- Um clique fixa o painel expandido; outro clique ou `Esc` volta ao modo
  compacto.
- O menu de contexto aberto com o botão direito contém a ação de saída.
- Apenas uma instância pode permanecer aberta por usuário.

O timer de animação existe somente durante os 180 ms de transição e é
interrompido ao atingir o tamanho final. Não há renderização contínua em
repouso.

## Regras para a primeira versão

- Providers publicam `MediaState`; não atualizam controles XAML diretamente.
- O provider Noctune tem prioridade apenas enquanto a conexão estiver ativa.
- O progresso é interpolado localmente a partir de uma âncora, sem polling em
  alta frequência do player.
- Animações devem parar completamente quando chegam ao estado final.
- Ausência ou falha do Glassline nunca pode bloquear o Noctune.

## Próximas implementações

1. Validar interoperabilidade e consumo de recursos em builds Release.
2. Completar localização e auditoria de acessibilidade.
3. Definir empacotamento e fluxo de atualização.

O protótipo registra marcos de startup, memória e CPU na saída de depuração. O
provider de demonstração continua disponível para desenvolvimento, enquanto a
composição padrão usa Noctune com fallback para SMTC. A medição deve ser feita
em build Release antes de confirmar o orçamento de recursos.

Orçamento provisório para a primeira medição Release em hardware de
desenvolvimento: janela pronta em até 1 segundo, working set em repouso abaixo
de 150 MiB e CPU normalizada em repouso abaixo de 1%. A telemetria coleta uma
única amostra de cinco segundos e não mantém monitoramento contínuo.

O provider SMTC já existe em Infrastructure e reage aos eventos de sessão,
metadados, reprodução e timeline, sem manter timer de consulta em repouso.
O provider Noctune implementa o framing v1, reconecta automaticamente e tem
prioridade sobre SMTC somente enquanto o pipe estiver conectado.
Capas locais do Noctune são exibidas quando o caminho existe. O progresso é
interpolado a partir da âncora em um timer que só permanece ativo durante a
reprodução e para imediatamente em pausa, stop ou unload.
A detecção de tela cheia usa eventos Win32 de foreground e mudança de posição;
não mantém polling ativo e restaura a cápsula sem tomar o foco do aplicativo.
Posição, aparência e ocultação em tela cheia são persistidas no armazenamento
local do pacote. A inicialização com o Windows usa `StartupTask` declarado no
manifesto e respeita o consentimento do usuário.
Thumbnails do SMTC são copiadas sob demanda para o cache local e reutilizadas
por faixa; erros de imagem não impedem a publicação do restante do estado.
O repositório Noctune contém o cliente Windows opcional, executado em worker
próprio, com snapshots, eventos incrementais, comandos e reconexão.
O cache de capas do SMTC conserva no máximo 64 arquivos e 128 MiB, removendo os
itens menos recentes somente quando uma capa é acessada ou adicionada.

No modo Noctune, `artworkPath` permanece nulo por enquanto. Compartilhar a arte
exigiria criar um arquivo de cache legível por outro processo; essa ampliação de
exposição de dados locais depende de autorização explícita do mantenedor.

A licença continua pendente e não deve ser adicionada sem uma decisão explícita
do mantenedor.
