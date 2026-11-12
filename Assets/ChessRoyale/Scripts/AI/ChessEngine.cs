using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ChessRoyale.AI
{
    public enum ChessAIDifficulty
    {
        Easy,
        Medium,
        Expert,
    }

    // A chess position the engine can search. Squares are numbered rank * 8 + file (a1 = 0, h8 = 63).
    // Pieces: 1 pawn, 2 knight, 3 bishop, 4 rook, 5 queen, 6 king; positive = White, negative = Black.
    public sealed class EnginePosition
    {
        public const int Pawn = 1, Knight = 2, Bishop = 3, Rook = 4, Queen = 5, King = 6;
        public const int WhiteKingside = 1, WhiteQueenside = 2, BlackKingside = 4, BlackQueenside = 8;

        public readonly int[] Board = new int[64];
        public int SideToMove = 1;     // +1 White, -1 Black
        public int Castling;           // WhiteKingside | WhiteQueenside | BlackKingside | BlackQueenside
        public int EnPassant = -1;     // square a pawn can capture onto, or -1
        public int Halfmove;           // moves since the last capture or pawn move (fifty-move rule)

        public EnginePosition Clone()
        {
            EnginePosition copy = new EnginePosition();
            Array.Copy(Board, copy.Board, 64);
            copy.SideToMove = SideToMove;
            copy.Castling = Castling;
            copy.EnPassant = EnPassant;
            copy.Halfmove = Halfmove;
            return copy;
        }

        // Standard FEN, used by the tests and handy for debugging.
        public static EnginePosition FromFen(string fen)
        {
            EnginePosition position = new EnginePosition();
            string[] parts = fen.Split(' ');
            int rank = 7, file = 0;
            foreach (char c in parts[0])
            {
                if (c == '/') { rank--; file = 0; continue; }
                if (char.IsDigit(c)) { file += c - '0'; continue; }
                int type = "pnbrqk".IndexOf(char.ToLowerInvariant(c)) + 1;
                position.Board[rank * 8 + file] = char.IsUpper(c) ? type : -type;
                file++;
            }

            position.SideToMove = parts.Length > 1 && parts[1] == "b" ? -1 : 1;
            if (parts.Length > 2)
            {
                if (parts[2].Contains("K")) position.Castling |= WhiteKingside;
                if (parts[2].Contains("Q")) position.Castling |= WhiteQueenside;
                if (parts[2].Contains("k")) position.Castling |= BlackKingside;
                if (parts[2].Contains("q")) position.Castling |= BlackQueenside;
            }

            if (parts.Length > 3 && parts[3] != "-")
                position.EnPassant = (parts[3][1] - '1') * 8 + (parts[3][0] - 'a');
            if (parts.Length > 4)
                int.TryParse(parts[4], out position.Halfmove);
            return position;
        }
    }

    // One move. Promotion is a piece type (2-5) or 0.
    public struct EngineMove
    {
        public int From;
        public int To;
        public int Promotion;

        public EngineMove(int from, int to, int promotion)
        {
            From = from;
            To = to;
            Promotion = promotion;
        }

        public bool IsValid => From != To;

        public override string ToString()
        {
            string text = $"{(char)('a' + From % 8)}{From / 8 + 1}{(char)('a' + To % 8)}{To / 8 + 1}";
            return Promotion == 0 ? text : text + " nbrq"[Promotion - 1];
        }
    }

    // Chess Royale's computer opponent: a classic alpha-beta search.
    //
    // It looks ahead move by move ("if I play this, they reply that..."), scores the positions it reaches
    // by material and piece placement, and skips lines that can't beat what it has already found.
    // Difficulty changes how far it looks and how often it settles for a weaker move on purpose.
    //
    // Pure C# with no Unity calls, so it can run on a background thread and be tested outside Unity.
    public sealed class ChessEngine
    {
        private const int Infinity = 1000000;
        private const int MateScore = 100000;
        private const int MaxPly = 64;

        private static readonly int[] PieceValues = { 0, 100, 320, 330, 500, 900, 0 };
        private static readonly int[] KnightJumps = { 17, 15, 10, 6, -6, -10, -15, -17 };
        private static readonly int[] KnightFileSteps = { 1, -1, 2, -2, 2, -2, 1, -1 };
        private static readonly int[] KingSteps = { 9, 8, 7, 1, -1, -7, -8, -9 };
        private static readonly int[] KingFileSteps = { 1, 0, -1, 1, -1, 1, 0, -1 };
        private static readonly int[] BishopDirections = { 9, 7, -7, -9 };
        private static readonly int[] BishopFileSteps = { 1, -1, 1, -1 };
        private static readonly int[] RookDirections = { 8, -8, 1, -1 };
        private static readonly int[] RookFileSteps = { 0, 0, 1, -1 };

        // Piece-square tables from White's point of view, rank 8 first (Tomasz Michniewski's
        // "simplified evaluation function"): knights like the center, kings like shelter, and so on.
        private static readonly int[] PawnTable =
        {
             0,  0,  0,  0,  0,  0,  0,  0,
            50, 50, 50, 50, 50, 50, 50, 50,
            10, 10, 20, 30, 30, 20, 10, 10,
             5,  5, 10, 25, 25, 10,  5,  5,
             0,  0,  0, 20, 20,  0,  0,  0,
             5, -5,-10,  0,  0,-10, -5,  5,
             5, 10, 10,-20,-20, 10, 10,  5,
             0,  0,  0,  0,  0,  0,  0,  0,
        };
        private static readonly int[] KnightTable =
        {
            -50,-40,-30,-30,-30,-30,-40,-50,
            -40,-20,  0,  0,  0,  0,-20,-40,
            -30,  0, 10, 15, 15, 10,  0,-30,
            -30,  5, 15, 20, 20, 15,  5,-30,
            -30,  0, 15, 20, 20, 15,  0,-30,
            -30,  5, 10, 15, 15, 10,  5,-30,
            -40,-20,  0,  5,  5,  0,-20,-40,
            -50,-40,-30,-30,-30,-30,-40,-50,
        };
        private static readonly int[] BishopTable =
        {
            -20,-10,-10,-10,-10,-10,-10,-20,
            -10,  0,  0,  0,  0,  0,  0,-10,
            -10,  0,  5, 10, 10,  5,  0,-10,
            -10,  5,  5, 10, 10,  5,  5,-10,
            -10,  0, 10, 10, 10, 10,  0,-10,
            -10, 10, 10, 10, 10, 10, 10,-10,
            -10,  5,  0,  0,  0,  0,  5,-10,
            -20,-10,-10,-10,-10,-10,-10,-20,
        };
        private static readonly int[] RookTable =
        {
             0,  0,  0,  0,  0,  0,  0,  0,
             5, 10, 10, 10, 10, 10, 10,  5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
             0,  0,  0,  5,  5,  0,  0,  0,
        };
        private static readonly int[] QueenTable =
        {
            -20,-10,-10, -5, -5,-10,-10,-20,
            -10,  0,  0,  0,  0,  0,  0,-10,
            -10,  0,  5,  5,  5,  5,  0,-10,
             -5,  0,  5,  5,  5,  5,  0, -5,
              0,  0,  5,  5,  5,  5,  0, -5,
            -10,  5,  5,  5,  5,  5,  0,-10,
            -10,  0,  5,  0,  0,  0,  0,-10,
            -20,-10,-10, -5, -5,-10,-10,-20,
        };
        private static readonly int[] KingMiddleTable =
        {
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -20,-30,-30,-40,-40,-30,-30,-20,
            -10,-20,-20,-20,-20,-20,-20,-10,
             20, 20,  0,  0,  0,  0, 20, 20,
             20, 30, 10,  0,  0, 10, 30, 20,
        };
        private static readonly int[] KingEndTable =
        {
            -50,-40,-30,-20,-20,-30,-40,-50,
            -30,-20,-10,  0,  0,-10,-20,-30,
            -30,-10, 20, 30, 30, 20,-10,-30,
            -30,-10, 30, 40, 40, 30,-10,-30,
            -30,-10, 30, 40, 40, 30,-10,-30,
            -30,-10, 20, 30, 30, 20,-10,-30,
            -30,-30,  0,  0,  0,  0,-30,-30,
            -50,-30,-30,-30,-30,-30,-30,-50,
        };

        // Castling rights that disappear when a piece leaves or lands on a square.
        private static readonly int[] CastlingMask = BuildCastlingMask();

        private readonly Random random;
        private readonly EngineMove[][] killers = new EngineMove[MaxPly][];
        private readonly List<EngineMove>[] moveLists = new List<EngineMove>[MaxPly + 16];
        // Reused sort keys, one buffer per move list, so the search allocates nothing while it runs
        // (garbage would make Unity's collector pause the whole game, which stutters in VR).
        private readonly int[][] orderKeys = new int[MaxPly + 17][];
        private EnginePosition position;
        private Stopwatch clock;
        private long timeLimitMs;
        private long nodes;
        private bool stopped;
        private volatile bool cancelRequested;

        public long NodesSearched => nodes;
        public int DepthReached { get; private set; }

        public ChessEngine(int seed)
        {
            random = new Random(seed);
            for (int i = 0; i < killers.Length; i++)
                killers[i] = new EngineMove[2];
            for (int i = 0; i < moveLists.Length; i++)
                moveLists[i] = new List<EngineMove>(64);
            for (int i = 0; i < orderKeys.Length; i++)
                orderKeys[i] = new int[256];
        }

        // Safe to call from another thread; the search returns its best move so far.
        public void Cancel()
        {
            cancelRequested = true;
        }

        // ---------- Public entry point ----------

        public EngineMove FindMove(EnginePosition start, ChessAIDifficulty difficulty)
        {
            position = start.Clone();
            cancelRequested = false;
            stopped = false;
            nodes = 0;
            DepthReached = 0;
            clock = Stopwatch.StartNew();

            List<EngineMove> legal = LegalMoves(position);
            if (legal.Count == 0)
                return default(EngineMove);
            if (legal.Count == 1)
                return legal[0];

            switch (difficulty)
            {
                case ChessAIDifficulty.Easy:
                    return PickEasy(legal);
                case ChessAIDifficulty.Medium:
                    return PickMedium(legal);
                default:
                    return PickExpert(legal);
            }
        }

        // Easy: looks one move ahead without checking replies, so it grabs free pieces but walks into
        // recaptures. It also picks from "good enough" moves and sometimes plays something random.
        private EngineMove PickEasy(List<EngineMove> legal)
        {
            timeLimitMs = 1000;
            if (random.NextDouble() < 0.2)
                return legal[random.Next(legal.Count)];

            List<KeyValuePair<EngineMove, int>> scored = ScoreRootMoves(legal, 1, false);
            return PickWithinMargin(scored, 150);
        }

        // Medium: three moves ahead, settles captures properly, with a little variety between equal moves.
        private EngineMove PickMedium(List<EngineMove> legal)
        {
            timeLimitMs = 1500;
            List<KeyValuePair<EngineMove, int>> scored = ScoreRootMoves(legal, 3, true);
            return PickWithinMargin(scored, 20);
        }

        // Expert: iterative deepening (2, 3, 4 ... moves ahead) until about two seconds have passed,
        // always keeping the best move from the deepest search that finished.
        private EngineMove PickExpert(List<EngineMove> legal)
        {
            timeLimitMs = 2000;
            EngineMove best = legal[0];
            OrderMoves(legal, 0, default(EngineMove), orderKeys.Length - 1);
            for (int depth = 1; depth <= 32; depth++)
            {
                EngineMove iterationBest = default(EngineMove);
                int alpha = -Infinity;
                foreach (EngineMove move in legal)
                {
                    Undo undo = Make(move);
                    int score = -Search(depth - 1, -Infinity, -alpha, 1);
                    Unmake(move, undo);
                    if (stopped)
                        break;
                    if (score > alpha)
                    {
                        alpha = score;
                        iterationBest = move;
                    }
                }

                if (stopped)
                {
                    // A partial iteration that already found something better than the old best is still useful.
                    if (iterationBest.IsValid && alpha > -Infinity)
                        best = iterationBest;
                    break;
                }

                best = iterationBest;
                DepthReached = depth;
                if (alpha >= MateScore - MaxPly)
                    break; // Found a forced mate; no need to look further.

                // Search the best move first next time; it makes the next, deeper search much faster.
                legal.Remove(best);
                legal.Insert(0, best);

                // Starting another iteration that is certain to run out of time just wastes the clock.
                if (clock.ElapsedMilliseconds > timeLimitMs / 2)
                    break;
            }

            return best;
        }

        private List<KeyValuePair<EngineMove, int>> ScoreRootMoves(List<EngineMove> legal, int depth, bool quiescence)
        {
            List<KeyValuePair<EngineMove, int>> scored = new List<KeyValuePair<EngineMove, int>>(legal.Count);
            foreach (EngineMove move in legal)
            {
                Undo undo = Make(move);
                int score = quiescence
                    ? -Search(depth - 1, -Infinity, Infinity, 1)
                    : -EvaluateShallow(depth - 1);
                Unmake(move, undo);
                if (stopped)
                    break; // Out of time: this move's score is unreliable, so leave it and the rest out.
                scored.Add(new KeyValuePair<EngineMove, int>(move, score));
            }

            DepthReached = depth;
            return scored;
        }

        // Plain one-ply look without quiescence: deliberately short-sighted for Easy.
        private int EvaluateShallow(int depth)
        {
            if (depth <= 0)
                return Evaluate();
            return Search(depth, -Infinity, Infinity, 1);
        }

        private EngineMove PickWithinMargin(List<KeyValuePair<EngineMove, int>> scored, int margin)
        {
            if (scored.Count == 0)
                return LegalMoves(position)[0];

            int best = -Infinity;
            foreach (KeyValuePair<EngineMove, int> entry in scored)
                best = Math.Max(best, entry.Value);

            // Never throw away a forced mate.
            if (best >= MateScore - MaxPly)
                margin = 0;

            List<EngineMove> candidates = new List<EngineMove>();
            foreach (KeyValuePair<EngineMove, int> entry in scored)
            {
                if (entry.Value >= best - margin)
                    candidates.Add(entry.Key);
            }

            return candidates[random.Next(candidates.Count)];
        }

        // ---------- Search ----------

        private int Search(int depth, int alpha, int beta, int ply)
        {
            if ((++nodes & 2047) == 0 && (cancelRequested || clock.ElapsedMilliseconds > timeLimitMs))
                stopped = true;
            if (stopped)
                return 0;

            if (position.Halfmove >= 100)
                return 0;

            bool inCheck = InCheck(position.SideToMove);
            if (inCheck)
                depth++; // Don't stop looking in the middle of a check.

            if (depth <= 0 || ply >= MaxPly)
                return Quiesce(alpha, beta, ply);

            List<EngineMove> moves = moveLists[ply];
            GenerateMoves(position, moves, false);
            OrderMoves(moves, ply, default(EngineMove), ply);

            int legalCount = 0;
            for (int i = 0; i < moves.Count; i++)
            {
                EngineMove move = moves[i];
                Undo undo = Make(move);
                if (InCheck(-position.SideToMove))
                {
                    Unmake(move, undo);
                    continue;
                }

                legalCount++;
                int score = -Search(depth - 1, -beta, -alpha, ply + 1);
                Unmake(move, undo);
                if (stopped)
                    return 0;

                if (score >= beta)
                {
                    if (undo.Captured == 0 && ply < MaxPly)
                    {
                        killers[ply][1] = killers[ply][0];
                        killers[ply][0] = move;
                    }
                    return beta;
                }

                if (score > alpha)
                    alpha = score;
            }

            if (legalCount == 0)
                return inCheck ? -MateScore + ply : 0; // Checkmate (prefer the fastest) or stalemate.

            return alpha;
        }

        // Keeps playing out captures before scoring, so it never judges a position in the middle of a trade.
        private int Quiesce(int alpha, int beta, int ply)
        {
            if ((++nodes & 2047) == 0 && (cancelRequested || clock.ElapsedMilliseconds > timeLimitMs))
                stopped = true;
            if (stopped)
                return 0;

            int standPat = Evaluate();
            if (standPat >= beta)
                return beta;
            if (standPat > alpha)
                alpha = standPat;
            if (ply >= MaxPly + 8)
                return alpha;

            int slot = Math.Min(ply, moveLists.Length - 1);
            List<EngineMove> moves = moveLists[slot];
            GenerateMoves(position, moves, true);
            OrderMoves(moves, MaxPly, default(EngineMove), slot);

            for (int i = 0; i < moves.Count; i++)
            {
                EngineMove move = moves[i];
                Undo undo = Make(move);
                if (InCheck(-position.SideToMove))
                {
                    Unmake(move, undo);
                    continue;
                }

                int score = -Quiesce(-beta, -alpha, ply + 1);
                Unmake(move, undo);
                if (stopped)
                    return 0;
                if (score >= beta)
                    return beta;
                if (score > alpha)
                    alpha = score;
            }

            return alpha;
        }

        // Captures first, biggest victim with the smallest attacker first; then promotions; then
        // "killer" quiet moves that caused cutoffs at this depth before.
        private void OrderMoves(List<EngineMove> moves, int ply, EngineMove first, int slot)
        {
            int[] keys = orderKeys[slot];
            if (keys.Length < moves.Count)
                keys = orderKeys[slot] = new int[moves.Count * 2];
            for (int i = 0; i < moves.Count; i++)
            {
                EngineMove move = moves[i];
                int key = 0;
                int victim = Math.Abs(position.Board[move.To]);
                if (victim == 0 && move.To == position.EnPassant && Math.Abs(position.Board[move.From]) == EnginePosition.Pawn)
                    victim = EnginePosition.Pawn;
                if (victim != 0)
                    key = 10000 + PieceValues[victim] * 10 - PieceValues[Math.Abs(position.Board[move.From])] / 10;
                if (move.Promotion != 0)
                    key += 9000 + PieceValues[move.Promotion];
                if (ply < MaxPly && key == 0)
                {
                    if (SameMove(move, killers[ply][0])) key = 8000;
                    else if (SameMove(move, killers[ply][1])) key = 7000;
                }
                if (first.IsValid && SameMove(move, first))
                    key = int.MaxValue;
                keys[i] = key;
            }

            // Insertion sort, highest key first; move lists are short.
            for (int i = 1; i < moves.Count; i++)
            {
                EngineMove move = moves[i];
                int key = keys[i];
                int j = i - 1;
                while (j >= 0 && keys[j] < key)
                {
                    moves[j + 1] = moves[j];
                    keys[j + 1] = keys[j];
                    j--;
                }
                moves[j + 1] = move;
                keys[j + 1] = key;
            }
        }

        private static bool SameMove(EngineMove a, EngineMove b)
        {
            return a.From == b.From && a.To == b.To && a.Promotion == b.Promotion;
        }

        // ---------- Evaluation (from the side to move's point of view) ----------

        private int Evaluate()
        {
            int score = 0;
            int nonPawnMaterial = 0;
            int whiteKing = -1, blackKing = -1;
            int[] board = position.Board;

            for (int square = 0; square < 64; square++)
            {
                int piece = board[square];
                if (piece == 0)
                    continue;

                int type = Math.Abs(piece);
                if (type == EnginePosition.King)
                {
                    if (piece > 0) whiteKing = square; else blackKing = square;
                    continue;
                }

                if (type != EnginePosition.Pawn)
                    nonPawnMaterial += PieceValues[type];

                int value = PieceValues[type] + Table(type)[TableIndex(square, piece > 0)];
                score += piece > 0 ? value : -value;
            }

            // The king hides in the opening and walks to the center once the heavy pieces are gone.
            bool endgame = nonPawnMaterial <= 1300;
            int[] kingTable = endgame ? KingEndTable : KingMiddleTable;
            if (whiteKing >= 0) score += kingTable[TableIndex(whiteKing, true)];
            if (blackKing >= 0) score -= kingTable[TableIndex(blackKing, false)];

            return score * position.SideToMove;
        }

        private static int[] Table(int type)
        {
            switch (type)
            {
                case EnginePosition.Pawn: return PawnTable;
                case EnginePosition.Knight: return KnightTable;
                case EnginePosition.Bishop: return BishopTable;
                case EnginePosition.Rook: return RookTable;
                default: return QueenTable;
            }
        }

        // Tables are written rank 8 first from White's view; Black reads them mirrored.
        private static int TableIndex(int square, bool white)
        {
            int file = square & 7;
            int rank = square >> 3;
            return white ? (7 - rank) * 8 + file : rank * 8 + file;
        }

        // ---------- Move generation ----------

        // Returns the position after a move (the original is left unchanged).
        public static EnginePosition Apply(EnginePosition start, EngineMove move)
        {
            ChessEngine helper = new ChessEngine(0);
            helper.position = start.Clone();
            helper.Make(move);
            return helper.position;
        }

        public static List<EngineMove> LegalMoves(EnginePosition start)
        {
            ChessEngine helper = new ChessEngine(0);
            helper.position = start.Clone();
            List<EngineMove> pseudo = new List<EngineMove>(64);
            GenerateMoves(helper.position, pseudo, false);
            List<EngineMove> legal = new List<EngineMove>(pseudo.Count);
            foreach (EngineMove move in pseudo)
            {
                Undo undo = helper.Make(move);
                if (!helper.InCheck(-helper.position.SideToMove))
                    legal.Add(move);
                helper.Unmake(move, undo);
            }

            return legal;
        }

        // Counts every legal move sequence to the given depth. The totals for standard test positions are
        // published, so a match proves the move generator handles castling, en passant and promotion.
        public static long Perft(EnginePosition start, int depth)
        {
            ChessEngine helper = new ChessEngine(0);
            helper.position = start.Clone();
            return helper.PerftRecursive(depth, 0);
        }

        private long PerftRecursive(int depth, int ply)
        {
            if (depth == 0)
                return 1;

            List<EngineMove> moves = moveLists[ply];
            GenerateMoves(position, moves, false);
            EngineMove[] snapshot = moves.ToArray();
            long count = 0;
            foreach (EngineMove move in snapshot)
            {
                Undo undo = Make(move);
                if (!InCheck(-position.SideToMove))
                    count += PerftRecursive(depth - 1, ply + 1);
                Unmake(move, undo);
            }

            return count;
        }

        private static void GenerateMoves(EnginePosition position, List<EngineMove> moves, bool capturesOnly)
        {
            moves.Clear();
            int side = position.SideToMove;
            int[] board = position.Board;

            for (int from = 0; from < 64; from++)
            {
                int piece = board[from];
                if (piece == 0 || Math.Sign(piece) != side)
                    continue;

                int file = from & 7;
                switch (Math.Abs(piece))
                {
                    case EnginePosition.Pawn:
                        GeneratePawnMoves(position, from, moves, capturesOnly);
                        break;
                    case EnginePosition.Knight:
                        for (int i = 0; i < 8; i++)
                            AddStep(position, from, file, KnightJumps[i], KnightFileSteps[i], moves, capturesOnly);
                        break;
                    case EnginePosition.Bishop:
                        AddSlides(position, from, BishopDirections, BishopFileSteps, moves, capturesOnly);
                        break;
                    case EnginePosition.Rook:
                        AddSlides(position, from, RookDirections, RookFileSteps, moves, capturesOnly);
                        break;
                    case EnginePosition.Queen:
                        AddSlides(position, from, BishopDirections, BishopFileSteps, moves, capturesOnly);
                        AddSlides(position, from, RookDirections, RookFileSteps, moves, capturesOnly);
                        break;
                    case EnginePosition.King:
                        for (int i = 0; i < 8; i++)
                            AddStep(position, from, file, KingSteps[i], KingFileSteps[i], moves, capturesOnly);
                        if (!capturesOnly)
                            AddCastling(position, from, moves);
                        break;
                }
            }
        }

        private static void GeneratePawnMoves(EnginePosition position, int from, List<EngineMove> moves, bool capturesOnly)
        {
            int side = position.SideToMove;
            int[] board = position.Board;
            int forward = side * 8;
            int file = from & 7;
            int rank = from >> 3;
            int startRank = side > 0 ? 1 : 6;
            int lastRank = side > 0 ? 7 : 0;

            int one = from + forward;
            if (one >= 0 && one < 64 && board[one] == 0)
            {
                if ((one >> 3) == lastRank)
                    AddPromotions(from, one, moves);
                else if (!capturesOnly)
                {
                    moves.Add(new EngineMove(from, one, 0));
                    int two = one + forward;
                    if (rank == startRank && board[two] == 0)
                        moves.Add(new EngineMove(from, two, 0));
                }
            }

            for (int fileStep = -1; fileStep <= 1; fileStep += 2)
            {
                int targetFile = file + fileStep;
                if (targetFile < 0 || targetFile > 7)
                    continue;

                int to = one + fileStep;
                if (to < 0 || to >= 64)
                    continue;

                int occupant = board[to];
                bool capture = occupant != 0 && Math.Sign(occupant) == -side && Math.Abs(occupant) != EnginePosition.King;
                if (capture)
                {
                    if ((to >> 3) == lastRank)
                        AddPromotions(from, to, moves);
                    else
                        moves.Add(new EngineMove(from, to, 0));
                }
                else if (to == position.EnPassant && occupant == 0)
                {
                    moves.Add(new EngineMove(from, to, 0));
                }
            }
        }

        private static void AddPromotions(int from, int to, List<EngineMove> moves)
        {
            moves.Add(new EngineMove(from, to, EnginePosition.Queen));
            moves.Add(new EngineMove(from, to, EnginePosition.Rook));
            moves.Add(new EngineMove(from, to, EnginePosition.Bishop));
            moves.Add(new EngineMove(from, to, EnginePosition.Knight));
        }

        private static void AddStep(EnginePosition position, int from, int file, int step, int fileStep,
            List<EngineMove> moves, bool capturesOnly)
        {
            int to = from + step;
            int toFile = file + fileStep;
            if (to < 0 || to >= 64 || toFile < 0 || toFile > 7)
                return;

            int occupant = position.Board[to];
            if (occupant == 0)
            {
                if (!capturesOnly)
                    moves.Add(new EngineMove(from, to, 0));
            }
            else if (Math.Sign(occupant) != position.SideToMove && Math.Abs(occupant) != EnginePosition.King)
            {
                moves.Add(new EngineMove(from, to, 0));
            }
        }

        private static void AddSlides(EnginePosition position, int from, int[] directions, int[] fileSteps,
            List<EngineMove> moves, bool capturesOnly)
        {
            for (int d = 0; d < directions.Length; d++)
            {
                int to = from;
                int file = from & 7;
                while (true)
                {
                    to += directions[d];
                    file += fileSteps[d];
                    if (to < 0 || to >= 64 || file < 0 || file > 7)
                        break;

                    int occupant = position.Board[to];
                    if (occupant == 0)
                    {
                        if (!capturesOnly)
                            moves.Add(new EngineMove(from, to, 0));
                        continue;
                    }

                    if (Math.Sign(occupant) != position.SideToMove && Math.Abs(occupant) != EnginePosition.King)
                        moves.Add(new EngineMove(from, to, 0));
                    break;
                }
            }
        }

        private static void AddCastling(EnginePosition position, int from, List<EngineMove> moves)
        {
            int side = position.SideToMove;
            int home = side > 0 ? 4 : 60;
            if (from != home || IsAttacked(position, home, -side))
                return;

            int kingside = side > 0 ? EnginePosition.WhiteKingside : EnginePosition.BlackKingside;
            int queenside = side > 0 ? EnginePosition.WhiteQueenside : EnginePosition.BlackQueenside;
            int[] board = position.Board;

            if ((position.Castling & kingside) != 0 && board[home + 1] == 0 && board[home + 2] == 0 &&
                board[home + 3] == side * EnginePosition.Rook &&
                !IsAttacked(position, home + 1, -side) && !IsAttacked(position, home + 2, -side))
                moves.Add(new EngineMove(home, home + 2, 0));

            if ((position.Castling & queenside) != 0 && board[home - 1] == 0 && board[home - 2] == 0 && board[home - 3] == 0 &&
                board[home - 4] == side * EnginePosition.Rook &&
                !IsAttacked(position, home - 1, -side) && !IsAttacked(position, home - 2, -side))
                moves.Add(new EngineMove(home, home - 2, 0));
        }

        // ---------- Attacks ----------

        private bool InCheck(int side)
        {
            int king = side * EnginePosition.King;
            int[] board = position.Board;
            for (int square = 0; square < 64; square++)
            {
                if (board[square] == king)
                    return IsAttacked(position, square, -side);
            }

            return true; // No king: treat as lost (never happens in a legal game).
        }

        public static bool IsAttacked(EnginePosition position, int square, int bySide)
        {
            int[] board = position.Board;
            int file = square & 7;

            // Pawns attack diagonally forward, so look diagonally backward from the target.
            int pawnRow = square - bySide * 8;
            if (pawnRow >= 0 && pawnRow < 64)
            {
                if (file > 0 && board[pawnRow - 1] == bySide * EnginePosition.Pawn) return true;
                if (file < 7 && board[pawnRow + 1] == bySide * EnginePosition.Pawn) return true;
            }

            for (int i = 0; i < 8; i++)
            {
                int from = square + KnightJumps[i];
                int fromFile = file + KnightFileSteps[i];
                if (from >= 0 && from < 64 && fromFile >= 0 && fromFile <= 7 && board[from] == bySide * EnginePosition.Knight)
                    return true;
            }

            for (int i = 0; i < 8; i++)
            {
                int from = square + KingSteps[i];
                int fromFile = file + KingFileSteps[i];
                if (from >= 0 && from < 64 && fromFile >= 0 && fromFile <= 7 && board[from] == bySide * EnginePosition.King)
                    return true;
            }

            if (RayHits(board, square, BishopDirections, BishopFileSteps, bySide * EnginePosition.Bishop, bySide * EnginePosition.Queen))
                return true;
            return RayHits(board, square, RookDirections, RookFileSteps, bySide * EnginePosition.Rook, bySide * EnginePosition.Queen);
        }

        private static bool RayHits(int[] board, int square, int[] directions, int[] fileSteps, int slider, int queen)
        {
            for (int d = 0; d < directions.Length; d++)
            {
                int at = square;
                int file = square & 7;
                while (true)
                {
                    at += directions[d];
                    file += fileSteps[d];
                    if (at < 0 || at >= 64 || file < 0 || file > 7)
                        break;
                    int occupant = board[at];
                    if (occupant == 0)
                        continue;
                    if (occupant == slider || occupant == queen)
                        return true;
                    break;
                }
            }

            return false;
        }

        // ---------- Make / unmake ----------

        private struct Undo
        {
            public int Captured;
            public int CapturedSquare;
            public int Castling;
            public int EnPassant;
            public int Halfmove;
        }

        private Undo Make(EngineMove move)
        {
            int[] board = position.Board;
            int piece = board[move.From];
            int side = Math.Sign(piece);
            int type = Math.Abs(piece);

            Undo undo = new Undo
            {
                Captured = board[move.To],
                CapturedSquare = move.To,
                Castling = position.Castling,
                EnPassant = position.EnPassant,
                Halfmove = position.Halfmove,
            };

            // En passant: the captured pawn sits beside the destination, not on it.
            if (type == EnginePosition.Pawn && move.To == position.EnPassant && board[move.To] == 0)
            {
                undo.CapturedSquare = move.To - side * 8;
                undo.Captured = board[undo.CapturedSquare];
                board[undo.CapturedSquare] = 0;
            }

            board[move.To] = move.Promotion != 0 ? side * move.Promotion : piece;
            board[move.From] = 0;

            // Castling also moves the rook.
            if (type == EnginePosition.King && Math.Abs(move.To - move.From) == 2)
            {
                bool kingside = move.To > move.From;
                int rookFrom = kingside ? move.From + 3 : move.From - 4;
                int rookTo = kingside ? move.From + 1 : move.From - 1;
                board[rookTo] = board[rookFrom];
                board[rookFrom] = 0;
            }

            position.EnPassant = type == EnginePosition.Pawn && Math.Abs(move.To - move.From) == 16
                ? (move.From + move.To) / 2
                : -1;
            position.Castling &= CastlingMask[move.From] & CastlingMask[move.To];
            position.Halfmove = type == EnginePosition.Pawn || undo.Captured != 0 ? 0 : position.Halfmove + 1;
            position.SideToMove = -side;
            return undo;
        }

        private void Unmake(EngineMove move, Undo undo)
        {
            int[] board = position.Board;
            int moved = board[move.To];
            int side = Math.Sign(moved);
            int original = move.Promotion != 0 ? side * EnginePosition.Pawn : moved;

            board[move.From] = original;
            board[move.To] = 0;
            board[undo.CapturedSquare] = undo.Captured;

            if (Math.Abs(original) == EnginePosition.King && Math.Abs(move.To - move.From) == 2)
            {
                bool kingside = move.To > move.From;
                int rookFrom = kingside ? move.From + 3 : move.From - 4;
                int rookTo = kingside ? move.From + 1 : move.From - 1;
                board[rookFrom] = board[rookTo];
                board[rookTo] = 0;
            }

            position.Castling = undo.Castling;
            position.EnPassant = undo.EnPassant;
            position.Halfmove = undo.Halfmove;
            position.SideToMove = side;
        }

        private static int[] BuildCastlingMask()
        {
            int[] mask = new int[64];
            for (int i = 0; i < 64; i++)
                mask[i] = 15;
            mask[0] &= ~EnginePosition.WhiteQueenside;
            mask[7] &= ~EnginePosition.WhiteKingside;
            mask[4] &= ~(EnginePosition.WhiteKingside | EnginePosition.WhiteQueenside);
            mask[56] &= ~EnginePosition.BlackQueenside;
            mask[63] &= ~EnginePosition.BlackKingside;
            mask[60] &= ~(EnginePosition.BlackKingside | EnginePosition.BlackQueenside);
            return mask;
        }
    }
}
