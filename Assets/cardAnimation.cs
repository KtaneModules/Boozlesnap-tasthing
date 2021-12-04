public partial class boozlesnap
{
    private class cardAnimation
    {
        public card card { get; set; }
        public int player { get; set; }

        public cardAnimation(card c, int p)
        {
            card = c;
            player = p;
        }
    }
}
