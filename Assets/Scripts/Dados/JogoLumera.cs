using UnityEngine;

// Um jogo da plataforma = um card no Menu. Crie em Create > Lumera > Jogo e adicione a lista do Area_Cards.
[CreateAssetMenu(fileName = "Jogo", menuName = "Lumera/Jogo")]
public sealed class JogoLumera : ScriptableObject
{
    [Tooltip("Identificador fixo do jogo (ex.: 002_Jump_Force). Nao mude depois de publicado: o save usa este id.")]
    public string id = "";
    [Tooltip("Nome da cena aberta pelo card. Precisa estar no Build Profile.")]
    public string cena = "";
    [Tooltip("Vazio = o card usa a capa padrao do Card_Tamplete do Menu.")]
    public Sprite capa;
    [Tooltip("Nome exibido no card, um por idioma. Vazio cai no ingles e depois no portugues.")]
    public TextoLocalizado nome;
    [Tooltip("Posicao padrao no Menu (menor vem antes). Filtros da cena podem reordenar.")]
    public int ordem;
    [Tooltip("Preco em moedas. 0 = gratis.")]
    [Min(0)] public int preco;

    public bool Gratis => preco <= 0;
}
