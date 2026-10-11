using System;

namespace qp {

    /// <summary>
    /// What a tournament backend lets a developer do to it. Separate from
    /// <see cref="ITournamentBackend"/> on purpose: that one is the contract the game runs on and
    /// every backend must answer it, while this one is a favour and a backend is free not to.
    ///
    /// Every call writes the SERVER's state, because that is the only state that counts — a score
    /// put on the client is replaced by the next sync, which is exactly what makes it worth
    /// trusting. The client sees none of it until it syncs again.
    ///
    /// <see cref="TournamentManager.Debug"/> is null when the backend in use does not offer this,
    /// which is the honest answer for an HTTP server without debug endpoints — the tools then say
    /// so instead of appearing to work.
    /// </summary>
    public interface ITournamentDebug {

        /// <summary>Move the tournament's window back, so time appears to have passed: rivals
        /// score what they would have scored and the countdown shrinks by the same amount.</summary>
        void SkipTime(TimeSpan by);

        /// <summary>Set the player's score outright, up or down.</summary>
        void SetScore(int score);

        /// <summary>
        /// Set the score that lands the player in <paramref name="place"/> when the tournament
        /// ends — 1 for first.
        ///
        /// Knowable in advance because every rival's final is decided the moment the group is
        /// built, so finishing Nth only means sitting between two known numbers. The climb to it
        /// still plays out: the rivals have not reached their finals yet.
        /// </summary>
        void SetFinalPlace(int place);

        /// <summary>End it now. The next sync closes and freezes it, exactly as the clock would.</summary>
        void EndNow();

        /// <summary>Throw this tournament away and open a new one, with a new group, starting now.</summary>
        void Restart();

        /// <summary>Forget everything, on both sides. The next sync starts from nothing.</summary>
        void Clear();
    }
}
