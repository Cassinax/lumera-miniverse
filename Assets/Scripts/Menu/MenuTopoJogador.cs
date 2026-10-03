using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Infos_Jogador no topo do Menu: moedas do save, nome e foto do Play Games. Sem Play Games, ficam o nome e a
// foto padrao que estao na cena.
[DisallowMultipleComponent]
public sealed class MenuTopoJogador : MonoBehaviour
{
    [SerializeField] TMP_Text textoMoedas;
    [SerializeField] TMP_Text textoNome;
    [SerializeField] Image foto;

    ObjetoMestre mestre;
    string nomePadrao;
    Sprite fotoPadrao, fotoCarregada;

    void Awake()
    {
        if (textoNome) nomePadrao = textoNome.text;
        if (foto) fotoPadrao = foto.sprite;
    }

    void OnEnable()
    {
        mestre = ObjetoMestre.Instancia;
        if (!mestre) return;
        if (mestre.Save) mestre.Save.MoedasAlteradas += AoMudarMoedas;
        if (mestre.PlayGames) mestre.PlayGames.PerfilAlterado += AtualizarPerfil;
        AoMudarMoedas(mestre.Save ? mestre.Save.ObterMoedas() : 0);
        AtualizarPerfil();
    }

    void OnDisable()
    {
        if (!mestre) return;
        if (mestre.Save) mestre.Save.MoedasAlteradas -= AoMudarMoedas;
        if (mestre.PlayGames) mestre.PlayGames.PerfilAlterado -= AtualizarPerfil;
    }

    void OnDestroy()
    {
        if (fotoCarregada) Destroy(fotoCarregada);
    }

    void AoMudarMoedas(long saldo)
    {
        if (textoMoedas) textoMoedas.text = saldo.ToString();
    }

    void AtualizarPerfil()
    {
        var playGames = mestre ? mestre.PlayGames : null;
        bool conectado = playGames && playGames.Conectado;
        if (textoNome) textoNome.text = conectado && !string.IsNullOrEmpty(playGames.NomeJogador) ? playGames.NomeJogador : nomePadrao;
        if (!foto) return;
        if (fotoCarregada) { Destroy(fotoCarregada); fotoCarregada = null; }
        var textura = conectado ? playGames.Foto : null;
        if (textura)
        {
            fotoCarregada = Sprite.Create(textura, new Rect(0, 0, textura.width, textura.height), new Vector2(0.5f, 0.5f));
            foto.sprite = fotoCarregada;
        }
        else foto.sprite = fotoPadrao;
    }
}
