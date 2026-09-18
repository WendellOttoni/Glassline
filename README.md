# Glassline

Uma barra flutuante, discreta e expansível para o Windows. Glassline funciona
sozinha com as sessões de mídia do sistema e, quando disponível, prioriza uma
integração direta e exata com o Noctune.

> Estado: fundação técnica e protótipo inicial da cápsula WinUI 3.

## Desenvolvimento

Pré-requisitos:

- Windows 10 versão 1809 ou superior;
- SDK do .NET 10;
- modo de desenvolvedor do Windows habilitado para executar o pacote local.

```powershell
dotnet restore Glassline.slnx
dotnet build Glassline.slnx
dotnet test Glassline.slnx
dotnet run --project src/Glassline.App/Glassline.App.csproj
```

O SDK usado pelo repositório está fixado em `global.json`. Veja detalhes de
arquitetura e fluxo de trabalho em
[docs/development.md](docs/development.md).

## Estrutura

```text
src/
├── Glassline.App/             # janela, XAML, interação e composição WinUI 3
├── Glassline.Core/            # modelo de mídia e contratos independentes da UI
└── Glassline.Infrastructure/  # providers SMTC/Noctune e serviços do Windows
tests/
└── Glassline.Core.Tests/      # testes rápidos do domínio
docs/                          # contexto, protocolo e decisões do projeto
```

## Princípios

- **Leve por padrão:** nenhuma animação ou consulta contínua quando a barra está
  inativa.
- **Utilidade antes de decoração:** música e ações rápidas primeiro; widgets
  genéricos ficam fora do núcleo inicial.
- **Independente:** pode controlar uma sessão de mídia do Windows sem Noctune.
- **Integração precisa:** ao conectar ao Noctune, usa IPC local em vez de tentar
  adivinhar qual aplicativo de mídia o Windows considera ativo.
- **Opcional:** Glassline nunca será uma dependência necessária para executar o
  Noctune.

## Primeira versão

- cápsula compacta e painel expandido;
- capa, faixa, artista e progresso;
- anterior, tocar/pausar e próxima;
- ocultar durante aplicativos em tela cheia;
- preferências de posição, aparência e inicialização opcional;
- provider SMTC para uso genérico e provider Noctune por named pipe.

## Arquitetura proposta

```text
                    ┌───────────────────────────┐
                    │        Glassline UI        │
                    │ C# + WinUI 3 (Windows-only)│
                    └─────────────┬─────────────┘
                                  │ estado normalizado
             ┌────────────────────┴────────────────────┐
             │                                         │
┌────────────▼────────────┐                ┌───────────▼───────────┐
│ Windows media provider  │                │  Noctune pipe provider │
│ SMTC: modo independente │                │ integração preferencial │
└─────────────────────────┘                └───────────┬───────────┘
                                                        │ named pipe local
                                            ┌───────────▼───────────┐
                                            │       Noctune          │
                                            └───────────────────────┘
```

O protocolo é documentado em [docs/noctune-ipc-v1.md](docs/noctune-ipc-v1.md).
O histórico de decisões e o contexto para continuar o trabalho estão em
[docs/project-context.md](docs/project-context.md).

## Decisões ainda abertas

- Medir memória, CPU em repouso e tempo de abertura do protótipo WinUI 3 antes
  de fixar a stack definitivamente.
- Definir a licença do repositório (a intenção é MIT).
- Definir nome e identidade visual finais.

## Origem da ideia

O projeto foi inspirado no padrão de interface popularmente chamado de
“Dynamic Island”, mas Glassline será uma experiência própria de Windows, sem
dependência de Python ou de outro aplicativo.
