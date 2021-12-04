using rnd = UnityEngine.Random;

public partial class boozlesnap
{
#pragma warning disable CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()
#pragma warning disable CS0660 // Type defines operator == or operator != but does not override Object.Equals(object o)
    private class card
#pragma warning restore CS0660 // Type defines operator == or operator != but does not override Object.Equals(object o)
#pragma warning restore CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()
    {
        public int group { get; set; }
        public int index { get; set; }
        public int color { get; set; }
        public int family { get; set; }
        public int count { get; set; }

        public card(int g, int c, int ct)
        {
            group = g;
            index = rnd.Range(0, 4);
            color = c;
            family = c / 3;
            count = ct;
        }

        public static bool operator ==(card a, card b)
        {
            return a.group == b.group && a.index == a.index && a.color == b.color && a.count == b.count;
        }
        public static bool operator !=(card a, card b)
        {
            return a.group != b.group && a.index != a.index && a.color != b.color && a.count != b.count;
        }
    }
}
