using System;
using System.Globalization;
using UnityEngine;

// Idiomas da plataforma Lumera. O save guarda o codigo ISO, nunca o numero do enum.
public enum IdiomaLumera { Portugues = 0, Ingles = 1, Espanhol = 2, Hindi = 3 }

public static class LocalizacaoLumera
{
    public static IdiomaLumera Atual => ObjetoMestre.Instancia ? ObjetoMestre.Instancia.Idioma : DoSistema();

    public static string Codigo(IdiomaLumera idioma) => idioma switch
    {
        IdiomaLumera.Ingles => "en",
        IdiomaLumera.Espanhol => "es",
        IdiomaLumera.Hindi => "hi",
        _ => "pt"
    };

    public static bool TentarConverter(string codigo, out IdiomaLumera idioma)
    {
        string normalizado = codigo?.Trim().ToLowerInvariant() ?? "";
        idioma = IdiomaLumera.Portugues;
        if (normalizado.StartsWith("pt")) return true;
        if (normalizado.StartsWith("en")) { idioma = IdiomaLumera.Ingles; return true; }
        if (normalizado.StartsWith("es")) { idioma = IdiomaLumera.Espanhol; return true; }
        if (normalizado.StartsWith("hi")) { idioma = IdiomaLumera.Hindi; return true; }
        return false;
    }

    // Primeira abertura: idioma do aparelho. Idiomas sem traducao caem no ingles.
    public static IdiomaLumera DoSistema()
    {
        switch (Application.systemLanguage)
        {
            case SystemLanguage.Portuguese: return IdiomaLumera.Portugues;
            case SystemLanguage.Spanish: return IdiomaLumera.Espanhol;
            case SystemLanguage.English: return IdiomaLumera.Ingles;
        }
        // SystemLanguage nao distingue o hindi em todas as plataformas; a cultura do sistema sim.
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "hi" ? IdiomaLumera.Hindi : IdiomaLumera.Ingles;
    }
}

// Um texto por idioma. Campo vazio cai no ingles e depois no portugues.
// O hindi precisa de fonte Devanagari: ate o Lumera ter uma, deixe o campo vazio para usar o ingles.
[Serializable]
public struct TextoLocalizado
{
    public string portugues, ingles, espanhol, hindi;

    public TextoLocalizado(string portugues, string ingles, string espanhol, string hindi = "")
    {
        this.portugues = portugues;
        this.ingles = ingles;
        this.espanhol = espanhol;
        this.hindi = hindi;
    }

    public string Obter(IdiomaLumera idioma)
    {
        string texto = idioma switch
        {
            IdiomaLumera.Ingles => ingles,
            IdiomaLumera.Espanhol => espanhol,
            IdiomaLumera.Hindi => hindi,
            _ => portugues
        };
        if (!string.IsNullOrEmpty(texto)) return texto;
        return !string.IsNullOrEmpty(ingles) ? ingles : portugues ?? "";
    }
}
