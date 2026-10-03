using System;
using System.Collections;
using UnityEngine;

// Filho "Audio_Controller" do Objeto Mestre: toca todo o audio da plataforma. Os jogos tem um controlador proprio,
// com a lista de sons, que pede a reproducao aqui.
// Tres canais independentes, que tocam ao mesmo tempo: musica, efeitos de cena e interface (botoes). Cada canal
// toca um som por vez: um som novo substitui o anterior com um fade curto, evitando sobreposicao, estouro e ruido.
// Volume geral = AudioListener (tudo). Volume de efeitos = efeitos de cena e interface. Musica segue so o geral.
[DisallowMultipleComponent]
public sealed class ControladorAudio : MonoBehaviour
{
    [Tooltip("Duracao padrao do fade de entrada/saida da musica, em segundos.")]
    [SerializeField, Min(0)] float fadeMusica = 1;
    [Tooltip("Fade de saida de um efeito interrompido por outro, em segundos (evita estalo).")]
    [SerializeField, Min(0)] float fadeCorte = 0.03f;

    public float VolumeGeral { get; private set; } = 1;
    public float VolumeEfeitos { get; private set; } = 1;
    public event Action VolumesAlterados;
    public AudioClip MusicaAtual => musica.Atual;

    SaveAdapter save;
    Canal musica, efeitos, interfaceUi;

    // Duas fontes por canal: a nova entra enquanto a antiga sai, sem cortes secos.
    sealed class Canal
    {
        readonly AudioSource[] fontes = new AudioSource[2];
        readonly Coroutine[] fades = new Coroutine[2];
        readonly MonoBehaviour dono;
        int ativa;
        public AudioClip Atual => fontes[ativa].isPlaying ? fontes[ativa].clip : null;

        public Canal(MonoBehaviour dono, string nome, bool loop)
        {
            this.dono = dono;
            for (int i = 0; i < 2; i++)
            {
                var objeto = new GameObject(nome + "_" + (i + 1));
                objeto.transform.SetParent(dono.transform, false);
                var fonte = objeto.AddComponent<AudioSource>();
                fonte.playOnAwake = false;
                fonte.loop = loop;
                fonte.spatialBlend = 0;
                fontes[i] = fonte;
            }
        }

        public void Tocar(AudioClip clip, float volume, float fadeEntrada, float fadeSaida, bool loop)
        {
            Parar(fadeSaida);
            ativa = 1 - ativa;
            var fonte = fontes[ativa];
            Interromper(ativa);
            fonte.clip = clip;
            fonte.loop = loop;
            fonte.volume = fadeEntrada > 0 ? 0 : volume;
            fonte.Play();
            if (fadeEntrada > 0) fades[ativa] = dono.StartCoroutine(Fade(fonte, volume, fadeEntrada, false));
        }

        public void Parar(float fade)
        {
            var fonte = fontes[ativa];
            Interromper(ativa);
            if (!fonte.isPlaying) return;
            if (fade <= 0) fonte.Stop();
            else fades[ativa] = dono.StartCoroutine(Fade(fonte, 0, fade, true));
        }

        void Interromper(int indice)
        {
            if (fades[indice] != null) dono.StopCoroutine(fades[indice]);
            fades[indice] = null;
        }

        static IEnumerator Fade(AudioSource fonte, float alvo, float duracao, bool pararNoFim)
        {
            float inicio = fonte.volume;
            for (float t = 0; t < duracao; t += Time.unscaledDeltaTime)
            {
                fonte.volume = Mathf.Lerp(inicio, alvo, t / duracao);
                yield return null;
            }
            fonte.volume = alvo;
            if (pararNoFim) fonte.Stop();
        }
    }

    void Awake()
    {
        musica = new Canal(this, "Musica", true);
        efeitos = new Canal(this, "Efeitos", false);
        interfaceUi = new Canal(this, "Interface", false);
    }

    public void Configurar(SaveAdapter adapter)
    {
        save = adapter;
        DefinirVolumeGeral(save ? save.ObterFloatConfig(SaveAdapter.CHAVE_VOLUME_GERAL, 1) : 1, false);
        DefinirVolumeEfeitos(save ? save.ObterFloatConfig(SaveAdapter.CHAVE_VOLUME_EFEITOS, 1) : 1, false);
    }

    public void DefinirVolumeGeral(float volume, bool salvar = true)
    {
        VolumeGeral = Mathf.Clamp01(volume);
        AudioListener.volume = VolumeGeral;
        if (salvar && save) save.SalvarConfig(SaveAdapter.CHAVE_VOLUME_GERAL, VolumeGeral);
        VolumesAlterados?.Invoke();
    }

    public void DefinirVolumeEfeitos(float volume, bool salvar = true)
    {
        VolumeEfeitos = Mathf.Clamp01(volume);
        if (salvar && save) save.SalvarConfig(SaveAdapter.CHAVE_VOLUME_EFEITOS, VolumeEfeitos);
        VolumesAlterados?.Invoke();
    }

    //---------- Musica

    // A mesma musica ja tocando continua sem reiniciar. fade < 0 usa o padrao.
    public void TocarMusica(AudioClip clip, float fade = -1)
    {
        if (!clip) { PararMusica(fade); return; }
        if (MusicaAtual == clip) return;
        float duracao = fade < 0 ? fadeMusica : fade;
        musica.Tocar(clip, 1, duracao, duracao, true);
    }

    public void PararMusica(float fade = -1) => musica.Parar(fade < 0 ? fadeMusica : fade);

    //---------- Efeitos de cena e interface

    public void TocarEfeito(AudioClip clip, float volume = 1)
    {
        if (clip) efeitos.Tocar(clip, Mathf.Clamp01(volume) * VolumeEfeitos, 0, fadeCorte, false);
    }

    public void TocarInterface(AudioClip clip, float volume = 1)
    {
        if (clip) interfaceUi.Tocar(clip, Mathf.Clamp01(volume) * VolumeEfeitos, 0, fadeCorte, false);
    }
}
