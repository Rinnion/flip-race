using UnityEngine;

namespace FlipRace.Presentation
{
    public enum MascotReaction { None, Success, Hit, Win }

    /// <summary>Presentation only: every hero uses the same Core collision geometry and rules.</summary>
    public static class CharacterRoster
    {
        public const int Count = 4;
        static readonly string[] Names = { "Листик", "Капелька", "Травник", "Цветок" };
        static readonly string[] Success = { "Поймал ветер!", "Вот это всплеск!", "Точная тропа!", "Солнечный разгон!" };
        static readonly string[] Hit = { "Ой, листопад!", "Кап-кап… ещё раз!", "Корешок под лапой!", "Лепестки помялись!" };
        static readonly string[] Slow = { "Ветер навстречу!", "Вязкая капля!", "Тропа тянет назад!", "Тяжёлый лепесток!" };
        static readonly string[] Win = { "Ветер на моей стороне!", "Приплыл первым!", "Тропа пройдена!", "Расцветаю на финише!" };

        public static int Normalize(int index) { return index < 0 || index >= Count ? 0 : index; }
        public static string Name(int characterIndex) { return Names[Normalize(characterIndex)]; }
        public static string SuccessText(int characterIndex) { return Success[Normalize(characterIndex)]; }
        public static string HitText(int characterIndex) { return Hit[Normalize(characterIndex)]; }
        public static string SlowText(int characterIndex) { return Slow[Normalize(characterIndex)]; }
        public static string WinText(int characterIndex) { return Win[Normalize(characterIndex)]; }
        public static Color ScarfColor(int playerIndex)
        {
            return playerIndex == 0 ? new Color(183/255f,161/255f,222/255f) : new Color(241/255f,171/255f,129/255f);
        }
    }
}
