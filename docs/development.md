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

## Regras para a primeira versão

- Providers publicam `MediaState`; não atualizam controles XAML diretamente.
- O provider Noctune tem prioridade apenas enquanto a conexão estiver ativa.
- O progresso é interpolado localmente a partir de uma âncora, sem polling em
  alta frequência do player.
- Animações devem parar completamente quando chegam ao estado final.
- Ausência ou falha do Glassline nunca pode bloquear o Noctune.

## Próximas implementações

1. Instrumentar startup, memória e CPU do protótipo.
2. Ligar a cápsula a um provider falso para validar o fluxo de estado.
3. Implementar o provider SMTC orientado a eventos.
4. Implementar o protocolo Noctune v1 e seus testes de framing.
5. Adicionar configurações, tela cheia e inicialização com o Windows.

A licença continua pendente e não deve ser adicionada sem uma decisão explícita
do mantenedor.
