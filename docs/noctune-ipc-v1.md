# Protocolo Noctune ↔ Glassline, versão 1

Este documento define uma integração opcional. A ausência de Glassline não
deve alterar a reprodução de áudio nem o comportamento da TUI do Noctune.

## Transporte

- Named pipe local do Windows.
- Nome: `glassline-noctune-v1-{USER_HASH}`, em que `USER_HASH` são os primeiros
  16 caracteres hexadecimais maiúsculos do SHA-256 de `DOMAIN\\usuário`,
  obtido das variáveis de ambiente `USERDOMAIN` e `USERNAME`.
- Mensagens UTF-8 com prefixo de tamanho de 32 bits, little-endian.
- Máximo de 1 MiB por mensagem.
- O servidor rejeita versões de protocolo desconhecidas e mensagens inválidas.

## Conexão

1. Glassline abre o servidor de pipe ao iniciar.
2. Noctune tenta conectar sem bloquear sua interface.
3. Após conectar, Noctune envia um `snapshot` completo.
4. Mudanças subsequentes são eventos incrementais.
5. Ao reconectar, o snapshot substitui qualquer estado antigo.

## Envelope

```json
{
  "version": 1,
  "type": "snapshot",
  "payload": {}
}
```

## Eventos do Noctune

`snapshot` e `track_changed` usam:

```json
{
  "trackId": "biblioteca:42",
  "title": "Nome da faixa",
  "artist": "Artista",
  "album": "Álbum",
  "artworkPath": "C:\\...\\cover.jpg",
  "durationMs": 215000,
  "positionMs": 42000,
  "sentAtUnixMs": 0,
  "playback": "playing"
}
```

`playback_changed` informa `playing`, `paused` ou `stopped`.

`position_anchor` atualiza `positionMs` e `sentAtUnixMs`. Glassline interpola
localmente o progresso enquanto a reprodução estiver ativa, sem pedir posições
em alta frequência ao Noctune.

## Comandos do Glassline

```json
{ "version": 1, "type": "command", "payload": { "id": "uuid", "name": "toggle_playback" } }
```

Comandos iniciais: `previous`, `toggle_playback`, `next`, `show_noctune`.
Noctune confirma o resultado com `command_result` e envia o novo estado:

```json
{ "version": 1, "type": "command_result", "payload": { "id": "uuid", "success": true, "error": null } }
```

Glassline correlaciona a confirmação pelo `id` e abandona o comando se a
conexão fechar ou se não houver resposta em dois segundos.

## Segurança

O servidor cria o pipe com a opção `CurrentUserOnly`; o pipe é local e deve
aceitar somente o usuário atual. Glassline não executa
comandos arbitrários, nem recebe caminhos ou URIs para abrir por meio deste
protocolo.
