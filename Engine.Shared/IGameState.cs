// Libs
using System.Collections.Generic;

namespace Engine.Shared;

public interface IMove {
  int FromSquare { get; }
  int ToSquare { get; }
}

public interface IGameState {
  bool IsWhiteToMove { get; }
  IEnumerable<IMove> GetLegalMoves();
  void MakeMove(IMove move);
  void UndoMove();
  int Evaluate();
  bool IsGameOver();
}