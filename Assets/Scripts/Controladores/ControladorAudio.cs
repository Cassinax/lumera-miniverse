using System;
using UnityEngine;

// Filho "Audio_Controller" do Objeto Mestre. Volume geral vai para o AudioListener (todo som do jogo);
// volume de efeitos multiplica os efeitos tocados por TocarEfeito.
[DisallowMultipleComponent]
public sealed class ControladorAudio : MonoBehaviour
{
    public float VolumeGeral { get; private set; } = 1;
    public float VolumeEfeitos { get; private set; } = 1;
    public event Action VolumesAlterados;

    SaveAdapter save;
    AudioSource fonteEfeitos;

    void Awake()
    {
        fonteEfeitos = GetComponent<AudioSource>();
        if (!fonteEfeitos) fonteEfeitos = gameObject.AddComponent<AudioSource>();
        fonteEfeitos.playOnAwake = false;
        fonteEfeitos.spatialBlend = 0;
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

    // Efeitos de qualquer cena passam por aqui para respeitar o volume de efeitos.
    public void TocarEfeito(AudioClip clip, float volume = 1)
    {
        if (clip && fonteEfeitos) fonteEfeitos.PlayOneShot(clip, Mathf.Clamp01(volume) * VolumeEfeitos);
    }
}
