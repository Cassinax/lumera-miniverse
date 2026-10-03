using System.Globalization;
using TMPro;
using UnityEngine;

namespace Lumera.JumpForce
{
    // Controladores_Cena/Plataforma: liga o Jump Force a plataforma Lumera (Objeto Mestre).
    // Recorde salvo (com destaque quando e batido), moedas coletadas na carteira, metricas da partida,
    // placar online e conquistas do Play Games. Sem Objeto Mestre (cena aberta direto), so mostra o recorde da partida.
    [DisallowMultipleComponent]
    public sealed class JumpForcePlataforma : MonoBehaviour
    {
        [Tooltip("Mesmo id do JogoLumera do Menu. O save do jogo fica sob este id.")]
        [SerializeField] string idJogo = "002_Jump_Force";
        [SerializeField] JumpForcePlayer player;
        [SerializeField] JumpForceScore placar;
        [SerializeField] JumpForceSpawner spawner;
        [SerializeField] JumpForceAudio audioCena;

        [Header("Recorde")]
        [Tooltip("Text_Placar_Recorde: mostra o recorde na mesma unidade do placar da partida.")]
        [SerializeField] TMP_Text textoRecorde;
        [SerializeField] Color corRecordeBatido = new Color(1f, 0.82f, 0.2f);
        [Tooltip("Pulso do texto ao bater o recorde, em segundos.")]
        [SerializeField, Min(0)] float duracaoPulso = 0.6f;
        [SerializeField, Min(1)] float escalaPulso = 1.25f;

        [Header("Play Games (ids do Play Console; vazio = nao envia)")]
        [Tooltip("Placar: maior altura em metros, inteiro arredondado para baixo.")]
        [SerializeField] string placarAltura = "";
        [SerializeField] string conquistaPrimeiraPartida = "";
        [SerializeField] string conquistaNivel30 = "";
        [SerializeField] string conquistaNivel60 = "";
        [SerializeField] string conquistaNivel90 = "";
        [Tooltip("Incremental: um passo por moeda coletada.")]
        [SerializeField] string conquistaMoedas = "";
        [Tooltip("Incremental: um passo por uso do trampolim.")]
        [SerializeField] string conquistaTrampolim = "";
        [SerializeField] string conquistaSobreviver = "";
        [Tooltip("Tempo de uma partida para a conquista de sobrevivencia, em segundos.")]
        [SerializeField, Min(1)] float segundosSobreviver = 300;

        const string ChaveRecorde = "recorde_m";
        float recordeSalvo, inicioPartida, pulsoAte;
        bool partidaAtiva, recordeBatido, sobreviveu;
        int moedasPartida, ultimaFaixa, ultimoNivelConquista;
        Color corOriginal;
        Vector3 escalaOriginal = Vector3.one;

        static ObjetoMestre Mestre => ObjetoMestre.Instancia;
        static SaveAdapter Save => Mestre ? Mestre.Save : null;
        static ControladorPlayGames PlayGames => Mestre ? Mestre.PlayGames : null;

        void Awake()
        {
            if (textoRecorde)
            {
                corOriginal = textoRecorde.color;
                escalaOriginal = textoRecorde.rectTransform.localScale;
            }
        }

        void Start()
        {
            if (Save && float.TryParse(Save.ObterDadoJogo(idJogo, ChaveRecorde, "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out float salvo))
                recordeSalvo = Mathf.Max(0, salvo);
            MostrarRecorde(recordeSalvo, false);
        }

        void OnEnable()
        {
            JumpForceEventos.Moeda += AoColetarMoeda;
            JumpForceEventos.Trampolim += AoUsarTrampolim;
            JumpForceEventos.Morte += AoMorrer;
        }

        void OnDisable()
        {
            JumpForceEventos.Moeda -= AoColetarMoeda;
            JumpForceEventos.Trampolim -= AoUsarTrampolim;
            JumpForceEventos.Morte -= AoMorrer;
        }

        void Update()
        {
            if (!player || !placar) return;
            if (player.GameplayEnabled && !player.Dead && !partidaAtiva) IniciarPartida();
            AnimarPulso();
            if (!partidaAtiva) return;

            float altura = placar.MaximumHeight;
            if (!recordeBatido && recordeSalvo > 0 && altura > recordeSalvo)
            {
                recordeBatido = true;
                pulsoAte = Time.unscaledTime + duracaoPulso;
                if (audioCena) audioCena.TocarRecorde();
            }
            if (altura > recordeSalvo) MostrarRecorde(altura, recordeBatido);

            int nivel = placar.Points;
            int faixa = spawner ? spawner.settings.TierIndex(nivel) : 0;
            if (faixa > ultimaFaixa)
            {
                ultimaFaixa = faixa;
                Registrar("faixa_alcancada", ("jogo", idJogo), ("faixa", faixa), ("nivel", nivel));
            }
            ConquistasDeNivel(nivel);
            if (!sobreviveu && Time.time - inicioPartida >= segundosSobreviver)
            {
                sobreviveu = true;
                if (PlayGames) PlayGames.DesbloquearConquista(conquistaSobreviver);
            }
        }

        void IniciarPartida()
        {
            partidaAtiva = true;
            recordeBatido = sobreviveu = false;
            inicioPartida = Time.time;
            moedasPartida = ultimaFaixa = 0;
            MostrarRecorde(recordeSalvo, false);
            Registrar("partida_iniciada", ("jogo", idJogo));
            if (PlayGames) PlayGames.DesbloquearConquista(conquistaPrimeiraPartida);
        }

        // causa: morte, menu ou saida. Grava o recorde e envia o placar.
        public void EncerrarPartida(string causa)
        {
            if (!partidaAtiva) return;
            partidaAtiva = false;
            float altura = placar ? placar.MaximumHeight : 0;
            bool novoRecorde = altura > recordeSalvo;
            if (novoRecorde) GravarRecorde(altura);
            Registrar("partida_terminada", ("jogo", idJogo), ("causa", causa), ("nivel", placar ? placar.Points : 0),
                ("altura_m", Mathf.FloorToInt(altura)), ("moedas", moedasPartida),
                ("duracao_s", Mathf.RoundToInt(Time.time - inicioPartida)), ("recorde", novoRecorde ? 1 : 0));
            if (PlayGames && altura > 0) PlayGames.EnviarPlacar(placarAltura, Mathf.FloorToInt(altura));
        }

        void GravarRecorde(float altura)
        {
            recordeSalvo = altura;
            if (!Save) return;
            Save.SalvarDadoJogo(idJogo, ChaveRecorde, altura.ToString("0.###", CultureInfo.InvariantCulture));
            Save.SalvarAgora();
        }

        void AoMorrer() => EncerrarPartida("morte");
        void OnDestroy() => EncerrarPartida("menu");
        void OnApplicationQuit() => EncerrarPartida("saida");

        // App para o fundo no meio da partida: o recorde ja alcancado nao se perde se o sistema fechar o app.
        void OnApplicationPause(bool pausado)
        {
            if (pausado && partidaAtiva && placar && placar.MaximumHeight > recordeSalvo) GravarRecorde(placar.MaximumHeight);
        }

        void AoColetarMoeda()
        {
            moedasPartida++;
            if (Save) Save.AdicionarMoedas(1, "gameplay", idJogo);
            if (PlayGames) PlayGames.IncrementarConquista(conquistaMoedas, 1);
        }

        void AoUsarTrampolim()
        {
            if (PlayGames) PlayGames.IncrementarConquista(conquistaTrampolim, 1);
        }

        void ConquistasDeNivel(int nivel)
        {
            if (!PlayGames || nivel <= ultimoNivelConquista) return;
            if (nivel >= 30 && ultimoNivelConquista < 30) PlayGames.DesbloquearConquista(conquistaNivel30);
            if (nivel >= 60 && ultimoNivelConquista < 60) PlayGames.DesbloquearConquista(conquistaNivel60);
            if (nivel >= 90 && ultimoNivelConquista < 90) PlayGames.DesbloquearConquista(conquistaNivel90);
            ultimoNivelConquista = nivel;
        }

        void MostrarRecorde(float metros, bool destaque)
        {
            if (!textoRecorde) return;
            float metrosPorPonto = placar ? Mathf.Max(0.1f, placar.metersPerPoint) : 1;
            textoRecorde.text = Mathf.FloorToInt(metros / metrosPorPonto).ToString();
            textoRecorde.color = destaque ? corRecordeBatido : corOriginal;
        }

        void AnimarPulso()
        {
            if (!textoRecorde) return;
            float restante = duracaoPulso > 0 ? Mathf.Clamp01((pulsoAte - Time.unscaledTime) / duracaoPulso) : 0;
            textoRecorde.rectTransform.localScale = escalaOriginal * Mathf.Lerp(1, escalaPulso, Mathf.Sin(restante * Mathf.PI));
        }

        void Registrar(string evento, params (string, object)[] valores)
        {
            if (Mestre) Mestre.RegistrarMetrica(evento, valores);
        }
    }
}
