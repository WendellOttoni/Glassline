# Contexto do projeto Glassline

Atualizado em 18 de setembro de 2026.

## Objetivo

Glassline é uma barra flutuante e expansível para Windows, inspirada no padrão
popularmente chamado de “Dynamic Island”, mas com identidade e implementação
próprias. Deve ser útil como aplicativo autônomo no Windows e, opcionalmente,
ter uma integração de primeira classe com o player de terminal Noctune.

O repositório do Noctune é irmão deste em `C:\Repo\Noctune` e é escrito em
Rust. Ele já expõe uma sessão de mídia do sistema no Windows por meio de SMTC
(System Media Transport Controls), usando `souvlaki`.

## Experiência inicial desejada

Versão 1, estritamente focada em música:

- cápsula compacta, discreta e sempre no topo;
- expansão ao passar o mouse ou clicar;
- capa, título, artista, estado de reprodução e progresso;
- controles de anterior, tocar/pausar e próxima;
- ocultar em tela cheia;
- posição, aparência e inicialização com Windows configuráveis;
- animações suaves, mas nenhuma renderização contínua em repouso.

Recursos como clima, calendário, tarefas, CPU/RAM, notificações genéricas e
letras sincronizadas ficam explicitamente fora da primeira versão.

## Por que este projeto existe

Foi estudado o repositório open source
[`rajsriv/dynamic-island-for-windows`](https://github.com/rajsriv/dynamic-island-for-windows),
em Python/PyQt6 e licença MIT. Ele é uma boa referência de interação e
estética, mas não deve ser portado nem usado como base de código:

- a classe principal concentra janela, renderização, animação, layout,
  preferências e funcionalidades não relacionadas;
- monitora a sessão de mídia *global* do Windows, portanto pode mostrar ou
  controlar Spotify/navegador em vez de Noctune;
- usa timers frequentes para animação, cursor e alguns monitores;
- depende de Python, PyQt e outras bibliotecas no ambiente de execução.

A ideia é reimplementar somente os requisitos úteis, do zero, em código
compilado.

## Stack sob avaliação

A primeira hipótese é **C# + WinUI 3 + Windows App SDK**.

Motivos:

- WinUI 3 é a stack nativa atual recomendada pela Microsoft para novos apps de
  desktop Windows;
- permite uma interface Windows 11 polida com composição, Mica/Acrylic,
  escala de DPI e acessibilidade sem construir tudo em Win32;
- pode ser distribuída por MSIX ou de forma self-contained, sem exigir que o
  usuário instale Python.

Trade-offs importantes:

- C# + WinUI 3 é um processo separado do Noctune, com custo próprio de startup,
  memória e runtime. Não adiciona esse custo ao processo Rust do Noctune;
- o runtime Windows App SDK precisa ser instalado pelo instalador ou incluído
  no aplicativo. A distribuição self-contained aumenta consideravelmente o
  tamanho do pacote;
- antes de assumir a stack definitiva, criar um protótipo mínimo e medir tempo
  de abertura, memória em idle, CPU em idle e CPU durante animação.

Alternativa caso o orçamento de recursos seja incompatível com WinUI 3:
**Rust + Win32/Direct2D/DirectComposition**, usando `windows-rs`. Essa opção
mantém Rust de ponta a ponta e pode ser mais enxuta, mas exige muito mais código
de UI e acabamento. Não há recomendação de usar Rust + WinUI 3 neste momento:
o suporte oficial e mais maduro de WinUI 3 é para C# e C++ com XAML.

## Modos de funcionamento

### Autônomo

Glassline usa `GlobalSystemMediaTransportControlsSessionManager` (SMTC) para
exibir e controlar a sessão de mídia atual do Windows. Assim pode ser útil para
Spotify, navegador, VLC e outros players compatíveis mesmo sem Noctune.

### Integrado ao Noctune

Quando o Noctune estiver rodando, Glassline deve preferir um named pipe local
para receber o estado exato dele e enviar comandos. Isso elimina a ambiguidade
da sessão global de mídia quando mais de um player estiver aberto.

O contrato inicial está em [noctune-ipc-v1.md](noctune-ipc-v1.md). Ele prevê:

- snapshot completo na conexão e após reconexão;
- eventos de faixa, estado e âncora de posição;
- interpolação local do progresso na ilha, sem polling do Noctune;
- comandos limitados a anterior, tocar/pausar, próxima e mostrar Noctune;
- versionamento, limite de tamanho e acesso apenas ao usuário atual.

Glassline é sempre opcional. Falha, ausência ou fechamento do Glassline nunca
podem impedir a reprodução ou a TUI do Noctune.

## Arquitetura proposta

```text
┌───────────────────────────────────────┐
│              Glassline UI              │
│  Window + animation + input + settings │
└───────────────┬───────────────────────┘
                │ MediaState normalizado
       ┌────────┴─────────┐
       │                  │
┌──────▼─────────┐  ┌─────▼────────────────┐
│ SMTC provider  │  │ Noctune pipe provider │
│ modo autônomo  │  │ modo preferencial     │
└────────────────┘  └─────────┬────────────┘
                               │ named pipe local
                     ┌─────────▼────────────┐
                     │        Noctune       │
                     └──────────────────────┘
```

Separar UI, providers e protocolo desde o início é intencional: uma eventual
troca de WinUI 3 para implementação Rust nativa não deve exigir alterações no
Noctune nem no protocolo.

## Decisões já tomadas

- Repositório separado: `WendellOttoni/Glassline`.
- Nome do produto: **Glassline**; evitar usar “Dynamic Island” como marca.
- Sem Python como requisito para o usuário final.
- Não depender do WinIsland: ele é escrito em Rust e possui API de plugins, mas
  o projeto host é GPL-3.0 e exigiria instalação de um app externo.
- Não iniciar com widgets de propósito geral; resolver mídia muito bem primeiro.
- Não iniciar com integração direta por FFI; named pipe é mais simples,
  desacoplado e funciona entre Rust e C#.

## Próximos passos sugeridos

1. Escolher a licença do Glassline. A intenção inicial é MIT, mas o arquivo
   `LICENSE` não deve ser criado sem decisão explícita do mantenedor.
2. Criar um app WinUI 3 mínimo, sem providers, que mostra uma cápsula compacta
   e alterna para painel expandido.
3. Adicionar telemetria local de desenvolvimento para medir startup, memória e
   CPU; definir um orçamento antes de ampliar o escopo.
4. Implementar o provider SMTC com modelo de estado normalizado e sem polling
   em alta frequência fora de uma animação visível.
5. Implementar o named-pipe provider conforme o contrato v1.
6. Só então alterar o Noctune para publicar o pipe de forma opcional e não
   bloqueante.

## Estado do repositório

O repositório foi clonado vazio em `C:\Repo\Glassline`. Neste momento ele
contém documentação de produto, arquitetura e protocolo; ainda não há projeto
C#, dependências ou código de produção.
