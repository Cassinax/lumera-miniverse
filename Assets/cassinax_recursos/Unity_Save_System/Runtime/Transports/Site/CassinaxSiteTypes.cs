// Cassinax Unity System Save - v1.0.0
// Contexto da pagina do portal e resultado da configuracao remota.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
namespace cassinax.savesystem
{
    /// <summary>Contexto publicado pela pagina do jogo no site Cassinax.</summary>
    public struct CassinaxSiteContexto
    {
        public int JogoId;
        public bool UsuarioLogado;
        public string CsrfToken;
        public string Idioma;
        public string Build;
        public string GameConfigKey;

        public bool PodeSalvar => UsuarioLogado && JogoId > 0;
    }

    /// <summary>Resultado da consulta de configuracao remota do jogo.</summary>
    public struct CassinaxGameConfigResult
    {
        public bool Success;
        public bool NotModified;
        public string Json;
        public string ETag;
        public string Error;
        public long StatusCode;
    }
}
