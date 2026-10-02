// Cassinax Unity System Save
// Version: v0.9.0
// Status: integration-pilot
//
// Ponte WebGL entre a pagina do site Cassinax e o CassinaxSiteSaveApi (C#).
// Le as variaveis que o index.php do jogo publica antes do loader, conforme o
// guia de implementacao de jogos WebGL do site.
// Nao faz nenhuma chamada de rede: apenas expoe dados ja presentes na pagina.

mergeInto(LibraryManager.library, {
  CassinaxObterContextoJson: function () {
    var meta = document.querySelector('meta[name="csrf-token"]');

    var ctx = {
      jogoId: window.JOGO_ID || 0,
      usuarioLogado: !!window.USUARIO_LOGADO,
      csrfToken: (meta && meta.content) || "",
      idioma: window.IDIOMA_PORTAL || "pt",
      build: String(window.JOGO_BUILD || ""),
      gameConfigKey: window.GAME_CONFIG_KEY || ""
    };

    var json = JSON.stringify(ctx);
    var bufferSize = lengthBytesUTF8(json) + 1;
    var buffer = _malloc(bufferSize);
    stringToUTF8(json, buffer, bufferSize);
    return buffer;
  }
});
