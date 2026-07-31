using Engine.Chess.Analysis;
using Engine.Chess.Board;
using Engine.Chess.Core;
using Engine.Chess.Play;
using Engine.Chess.Search;

namespace Engine.UI.Services;

/// <summary>What the session is currently doing, which decides what the board will accept.</summary>
public enum SessionState {
    AwaitingPlayer,
    BotThinking,
    AwaitingPromotion,
    Finished,
    Reviewing,
}

/// <summary>A time control, or unlimited when <see cref="InitialSeconds"/> is zero.</summary>
public sealed record TimeControl(string Name, int InitialSeconds, int IncrementSeconds) {
    public bool IsUnlimited => InitialSeconds <= 0;

    public static readonly TimeControl Unlimited = new("Unlimited", 0, 0);

    public static IReadOnlyList<TimeControl> All { get; } = [
        new("3 + 2", 180, 2),
        new("5 + 0", 300, 0),
        new("10 + 0", 600, 0),
        Unlimited,
    ];
}

/// <summary>
/// Holds one game against a bot and drives everything around it: the bot's turn,
/// the running evaluation behind the bar, the clocks and the post-game review.
/// </summary>
/// <remarks>
/// WebAssembly runs on a single thread, so a search that ran to completion in one
/// call would freeze the page for its whole duration. Every long operation here is
/// therefore stepped: the engine yields after each completed depth, the session
/// raises <see cref="Changed"/>, and the browser gets a chance to paint in between.
/// </remarks>
public sealed class GameSession : IDisposable {
    /// <summary>Kept shallow enough that the bar updates between moves without stalling play.</summary>
    private static readonly SearchLimits BarLimits = new() { MaxDepth = 7, MaxTimeMilliseconds = 140 };

    private readonly SearchEngine _analysis = new(16);
    private readonly List<int> _evaluations = [];
    private readonly List<ReviewedMove> _reviewed = [];

    private CancellationTokenSource _turn = new();
    private ChessBot _bot;
    private DateTime _turnStartedUtc = DateTime.UtcNow;
    private double _whiteSecondsLeft;
    private double _blackSecondsLeft;
    private bool _clockRunning;

    public GameSession() {
        Bot = BotProfile.Default;
        _bot = new ChessBot(Bot);
        Game = new ChessGame();
        BoardPosition = Game.Position;
        ResetClocks();
    }

    /// <summary>Raised whenever anything the interface renders has changed.</summary>
    public event Action? Changed;

    /// <summary>Raised when a move lands, so the host can play the matching sound.</summary>
    public event Action<string>? SoundRequested;

    public ChessGame Game { get; private set; }

    public BotProfile Bot { get; private set; }

    public TimeControl TimeControl { get; private set; } = TimeControl.Unlimited;

    /// <summary>The colour the person is playing. The bot takes the other side.</summary>
    public Color PlayerColor { get; private set; } = Color.White;

    public SessionState State { get; private set; } = SessionState.AwaitingPlayer;

    public bool BoardFlipped { get; private set; }

    /// <summary>Live search output from the bot's current turn, or the last one it took.</summary>
    public SearchProgress? Telemetry { get; private set; }

    /// <summary>Evaluation after each ply in centipawns from white's point of view.</summary>
    public IReadOnlyList<int> Evaluations => _evaluations;

    /// <summary>The move number the board is showing. Equal to the move count during play.</summary>
    public int ViewingPly { get; private set; }

    /// <summary>The position drawn on the board, which may be an earlier one while browsing.</summary>
    public Position BoardPosition { get; private set; }

    /// <summary>A pending promotion waiting on the player's choice of piece.</summary>
    public (int From, int To)? PendingPromotion { get; private set; }

    /// <summary>The move suggested by the hint button, cleared as soon as anything is played.</summary>
    public Move Hint { get; private set; } = Move.None;

    public bool IsFindingHint { get; private set; }

    /// <summary>
    /// A move queued while the bot is still thinking, played the instant it becomes
    /// the player's turn. It cannot be validated when it is set, because the position
    /// it will apply to does not exist yet, so it is checked on the way out instead.
    /// </summary>
    public (int From, int To)? Premove { get; private set; }

    /// <summary>True when a move can be queued: the bot is thinking and the game is live.</summary>
    public bool CanPremove =>
        State == SessionState.BotThinking && !Game.IsOver && !IsBrowsingHistory;

    public GameReport? Review { get; private set; }

    /// <summary>Plies reviewed so far, for the review progress bar.</summary>
    public int ReviewProgress { get; private set; }

    public bool IsPlayerTurn =>
        State == SessionState.AwaitingPlayer && Game.SideToMove == PlayerColor && !Game.IsOver;

    /// <summary>True while the board is showing an earlier position, which disables moving.</summary>
    public bool IsBrowsingHistory => ViewingPly != Game.History.Count;

    public bool CanMove => IsPlayerTurn && !IsBrowsingHistory;

    /// <summary>Current evaluation in centipawns from white's point of view.</summary>
    public int CurrentEvaluation => _evaluations.Count > 0 ? _evaluations[^1] : 0;

    public TimeSpan WhiteClock => TimeSpan.FromSeconds(Math.Max(0, RemainingSeconds(Color.White)));

    public TimeSpan BlackClock => TimeSpan.FromSeconds(Math.Max(0, RemainingSeconds(Color.Black)));

    // ---------------------------------------------------------------- lifecycle

    public async Task StartNewGameAsync(BotProfile bot, Color playerColor, TimeControl timeControl) {
        await CancelTurnAsync();

        Bot = bot;
        PlayerColor = playerColor;
        TimeControl = timeControl;
        BoardFlipped = playerColor == Color.Black;

        _bot = new ChessBot(bot);
        Game = new ChessGame();
        _evaluations.Clear();
        _reviewed.Clear();
        Review = null;
        ReviewProgress = 0;
        Telemetry = null;
        PendingPromotion = null;
        Premove = null;
        Hint = Move.None;
        ViewingPly = 0;
        BoardPosition = Game.Position;
        State = SessionState.AwaitingPlayer;

        ResetClocks();
        StartClock();
        Notify();

        if (Game.SideToMove != PlayerColor) await RunBotTurnAsync();
    }

    /// <summary>
    /// Plays the player's move. Returns false when the move is not legal or it is
    /// not the player's turn, so the caller can snap a dragged piece back.
    /// </summary>
    public async Task<bool> TryPlayAsync(int from, int to, PieceType promotion = PieceType.None) {
        if (!CanMove) return false;

        IReadOnlyList<Move> matches = Game.MovesBetween(from, to);
        if (matches.Count == 0) return false;

        Move move;
        if (matches[0].IsPromotion) {
            if (promotion == PieceType.None) {
                // Ask which piece before committing, rather than assuming a queen.
                PendingPromotion = (from, to);
                State = SessionState.AwaitingPromotion;
                Notify();
                return true;
            }
            move = matches.FirstOrDefault(candidate => candidate.PromotionPiece == promotion, matches[0]);
        } else {
            move = matches[0];
        }

        return await PlayAsync(move);
    }

    public async Task CompletePromotionAsync(PieceType promotion) {
        if (PendingPromotion is not var (from, to)) return;

        PendingPromotion = null;
        State = SessionState.AwaitingPlayer;
        await TryPlayAsync(from, to, promotion);
    }

    public void CancelPromotion() {
        PendingPromotion = null;
        State = SessionState.AwaitingPlayer;
        Notify();
    }

    // ---------------------------------------------------------------- hint and premove

    /// <summary>
    /// Asks the engine what it would play here. Deliberately a real search rather
    /// than a book lookup, so the hint is right in any position, not just theory.
    /// </summary>
    public async Task ShowHintAsync() {
        if (!CanMove || IsFindingHint) return;

        IsFindingHint = true;
        Hint = Move.None;
        Notify();

        Move best = Move.None;
        var limits = new SearchLimits { MaxDepth = 10, MaxTimeMilliseconds = 600 };
        foreach (SearchResult iteration in _analysis.SearchIterations(Game.Position, limits, _turn.Token)) {
            best = iteration.BestMove;
            await Task.Yield();
        }

        Hint = best;
        IsFindingHint = false;
        Notify();
    }

    public void ClearHint() {
        if (Hint.IsNull) return;
        Hint = Move.None;
        Notify();
    }

    /// <summary>
    /// Queues a move to play as soon as the bot replies. Only the origin can be
    /// checked now: it must hold one of the player's pieces.
    /// </summary>
    public void SetPremove(int from, int to) {
        if (!CanPremove) return;

        Piece piece = Game.Position.PieceAt(from);
        if (piece == Piece.None || piece.ColorOf() != PlayerColor) return;

        Premove = (from, to);
        Notify();
    }

    public void ClearPremove() {
        if (Premove is null) return;
        Premove = null;
        Notify();
    }

    /// <summary>
    /// Plays the queued move if it turned out to be legal, and quietly drops it if
    /// the bot's reply made it impossible, which is what a premove is expected to do.
    /// </summary>
    private async Task TryPlayPremoveAsync() {
        if (Premove is not var (from, to)) return;

        Premove = null;
        Notify();

        if (!CanMove) return;
        // Premoves promote to a queen: there is no way to ask before the fact.
        await TryPlayAsync(from, to, PieceType.Queen);
    }

    private async Task<bool> PlayAsync(Move move) {
        Color mover = Game.SideToMove;
        if (!Game.TryMakeMove(move)) return false;

        Hint = Move.None;
        AddIncrement(mover);
        PlaySoundFor(move);
        ViewingPly = Game.History.Count;
        BoardPosition = Game.Position;
        Notify();

        await UpdateEvaluationAsync();

        if (CheckGameOver()) return true;

        await RunBotTurnAsync();
        return true;
    }

    private async Task RunBotTurnAsync() {
        if (Game.IsOver || Game.SideToMove == PlayerColor) return;

        State = SessionState.BotThinking;
        Telemetry = null;
        Notify();

        // Let the browser paint the thinking state before the search takes the thread.
        await Task.Yield();

        CancellationToken token = _turn.Token;
        BotMove choice;
        try {
            choice = await _bot.ChooseMoveAsync(Game.Position, OnSearchProgress, token);
        } catch (OperationCanceledException) {
            return;
        }

        if (token.IsCancellationRequested || choice.Move.IsNull) {
            State = SessionState.AwaitingPlayer;
            Notify();
            return;
        }

        Color mover = Game.SideToMove;
        if (!Game.TryMakeMove(choice.Move)) {
            State = SessionState.AwaitingPlayer;
            Notify();
            return;
        }

        AddIncrement(mover);
        PlaySoundFor(choice.Move);
        ViewingPly = Game.History.Count;
        BoardPosition = Game.Position;
        State = SessionState.AwaitingPlayer;
        Notify();

        await UpdateEvaluationAsync();

        if (CheckGameOver()) {
            Premove = null;
            return;
        }

        await TryPlayPremoveAsync();
    }

    private void OnSearchProgress(SearchProgress progress) {
        Telemetry = progress;
        Notify();
    }

    // ---------------------------------------------------------------- controls

    public async Task TakeBackAsync() {
        if (Game.History.Count == 0 || State == SessionState.Reviewing) return;

        await CancelTurnAsync();

        // Take back to the player's turn, which is one ply if the bot has not
        // replied yet and two once it has.
        Game.TryUndo();
        if (Game.History.Count > 0 && Game.SideToMove != PlayerColor) Game.TryUndo();

        while (_evaluations.Count > Game.History.Count) _evaluations.RemoveAt(_evaluations.Count - 1);

        Review = null;
        _reviewed.Clear();
        Premove = null;
        Hint = Move.None;
        ViewingPly = Game.History.Count;
        BoardPosition = Game.Position;
        State = SessionState.AwaitingPlayer;
        StartClock();
        Notify();

        if (Game.SideToMove != PlayerColor && !Game.IsOver) await RunBotTurnAsync();
    }

    public async Task ResignAsync() {
        await CancelTurnAsync();
        Game.Resign(PlayerColor);
        FinishGame();
    }

    public void FlipBoard() {
        BoardFlipped = !BoardFlipped;
        Notify();
    }

    /// <summary>Moves the board view to a given ply. Pass the move count to return to the live position.</summary>
    public void ViewPly(int ply) {
        ViewingPly = Math.Clamp(ply, 0, Game.History.Count);
        BoardPosition = ViewingPly == Game.History.Count ? Game.Position : Game.PositionAfterPly(ViewingPly);
        Notify();
    }

    public void StepView(int delta) => ViewPly(ViewingPly + delta);

    /// <summary>Records elapsed time and ends the game if a clock has run out.</summary>
    public void Tick() {
        if (!_clockRunning || TimeControl.IsUnlimited || Game.IsOver) return;

        if (RemainingSeconds(Game.SideToMove) <= 0) {
            Game.DeclareTimeout(Game.SideToMove);
            FinishGame();
            return;
        }
        Notify();
    }

    // ---------------------------------------------------------------- review

    /// <summary>
    /// Reviews the finished game one move at a time, updating <see cref="ReviewProgress"/>
    /// and yielding after each so the progress bar actually moves.
    /// </summary>
    public async Task RunReviewAsync() {
        if (Game.History.Count == 0 || State == SessionState.Reviewing) return;

        State = SessionState.Reviewing;
        _reviewed.Clear();
        ReviewProgress = 0;
        Review = null;
        Notify();

        // Shallower than an offline review would run: this has to finish in a
        // browser tab while the person waits.
        var review = new GameReview(depth: 8, millisecondsPerMove: 200);

        foreach (ReviewedMove move in review.ReviewIncrementally(
                     new Position(Game.StartingFen), Game.MoveSequence, _turn.Token)) {
            _reviewed.Add(move);
            ReviewProgress = _reviewed.Count;
            Notify();
            await Task.Yield();
        }

        Review = GameReview.Summarise(_reviewed);
        State = SessionState.Finished;
        Notify();
    }

    /// <summary>The verdict on a given ply, once the game has been reviewed.</summary>
    public ReviewedMove? ReviewedAt(int ply) =>
        ply >= 0 && ply < _reviewed.Count ? _reviewed[ply] : null;

    // ---------------------------------------------------------------- internals

    private async Task UpdateEvaluationAsync() {
        if (Game.IsOver) {
            _evaluations.Add(ResultEvaluation());
            Notify();
            return;
        }

        // The bar only needs a number, so this is deliberately shallow, and it is
        // stepped depth by depth rather than run in one call: a single blocking
        // search here would stall the page just as the player finished moving,
        // which reads as the board lagging behind the click.
        int whiteScore = _evaluations.Count > 0 ? _evaluations[^1] : 0;
        foreach (SearchResult iteration in _analysis.SearchIterations(Game.Position, BarLimits, _turn.Token)) {
            whiteScore = Game.SideToMove == Color.White ? iteration.Score : -iteration.Score;
            await Task.Yield();
        }

        _evaluations.Add(whiteScore);
        Notify();
    }

    private int ResultEvaluation() => Game.Result switch {
        GameResult.WhiteWinsByCheckmate or GameResult.WhiteWinsOnTime or GameResult.BlackResigned =>
            SearchScores.Mate,
        GameResult.BlackWinsByCheckmate or GameResult.BlackWinsOnTime or GameResult.WhiteResigned =>
            -SearchScores.Mate,
        _ => 0,
    };

    private bool CheckGameOver() {
        if (!Game.IsOver) return false;
        FinishGame();
        return true;
    }

    private void FinishGame() {
        _clockRunning = false;
        State = SessionState.Finished;
        SoundRequested?.Invoke("gameEnd");
        Notify();
    }

    private void PlaySoundFor(Move move) {
        string sound = Game.Position.IsInCheck ? "check"
            : move.IsPromotion ? "promote"
            : move.IsCastle ? "castle"
            : move.IsCapture ? "capture"
            : "move";
        SoundRequested?.Invoke(sound);
    }

    private void ResetClocks() {
        _whiteSecondsLeft = TimeControl.InitialSeconds;
        _blackSecondsLeft = TimeControl.InitialSeconds;
        _clockRunning = false;
        _turnStartedUtc = DateTime.UtcNow;
        BoardPosition = Game.Position;
    }

    private void StartClock() {
        _clockRunning = !TimeControl.IsUnlimited;
        _turnStartedUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Time is measured against a wall-clock timestamp rather than accumulated from
    /// ticks, so a turn during which the thread was busy searching is still charged
    /// correctly.
    /// </summary>
    private double RemainingSeconds(Color side) {
        double stored = side == Color.White ? _whiteSecondsLeft : _blackSecondsLeft;
        if (TimeControl.IsUnlimited) return stored;
        if (!_clockRunning || Game.SideToMove != side || Game.IsOver) return stored;
        return stored - (DateTime.UtcNow - _turnStartedUtc).TotalSeconds;
    }

    private void AddIncrement(Color mover) {
        if (TimeControl.IsUnlimited) {
            _turnStartedUtc = DateTime.UtcNow;
            return;
        }

        double remaining = Math.Max(0, RemainingSeconds(mover)) + TimeControl.IncrementSeconds;
        if (mover == Color.White) _whiteSecondsLeft = remaining;
        else _blackSecondsLeft = remaining;

        _clockRunning = true;
        _turnStartedUtc = DateTime.UtcNow;
    }

    private async Task CancelTurnAsync() {
        await _turn.CancelAsync();
        _turn.Dispose();
        _turn = new CancellationTokenSource();
    }

    private void Notify() => Changed?.Invoke();

    public void Dispose() {
        _turn.Cancel();
        _turn.Dispose();
    }
}
