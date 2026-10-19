namespace QueensPuzzle
{
    public enum HintKind
    {
        WrongQueen,   // a placed queen that isn't a real solution cell — remove it
        WrongX,       // an X on a cell that must hold a queen — clear it
        PlaceQueen,   // a cell that is forced to be a queen
        Eliminate,    // cells that can be X'd by the next deduction
        Guess         // no forced move — the best place to guess (fewest options), cells = those options
    }

    /// <summary>One suggested next action for the player. <see cref="cells"/> are board indices (row*n + col).</summary>
    public struct Hint
    {
        public HintKind kind;
        public int[] cells;       // the cells to mark/unmark — the step's targets
        public int[] causeCells;  // the cells the reasoning is ABOUT (shown tinted, never editable); may be null
        public string note;
        // Which deduction the step is — for the level trace only, never shown. Trial = no
        // deduction found: an X proven by contradiction (size 0) or a queen just shown (size 1).
        public SolveTechnique technique;
        public int size;          // the trick's k (a subset of k lines/regions); 0 when it has none
    }
}
