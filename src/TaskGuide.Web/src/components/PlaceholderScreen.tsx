import { ScreenNav } from './shared/ScreenNav'

interface PlaceholderScreenProps {
  title: string
}

// Static chrome only — these three tabs (Now / Schedule / More) are stubbed out for the
// walking skeleton (#51). The one screen that matters here is Tasks. Still renders through
// ScreenNav so the accent circle appears here too — a placeholder tab is a screen like any other.
export function PlaceholderScreen({ title }: PlaceholderScreenProps) {
  return (
    <>
      <ScreenNav title={title} />
      <div className="scroll">
        <div className="empty">Not built yet — this tab is chrome only for the walking skeleton.</div>
      </div>
    </>
  )
}
