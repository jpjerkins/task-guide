interface PlaceholderScreenProps {
  title: string
}

// Static chrome only — these three tabs (Now / Schedule / More) are stubbed out for the
// walking skeleton (#51). The one screen that matters here is Tasks.
export function PlaceholderScreen({ title }: PlaceholderScreenProps) {
  return (
    <div className="nav">
      <div className="nav-main">
        <h1>{title}</h1>
      </div>
      <div className="scroll">
        <div className="empty">Not built yet — this tab is chrome only for the walking skeleton.</div>
      </div>
    </div>
  )
}
