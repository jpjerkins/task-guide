import { createContext, useContext, type ReactNode } from 'react'
import { quickAction } from './screenRegistry'

interface ScreenNavBack {
  label?: string
  onBack: () => void
}

interface ScreenNavProps {
  title: string
  sub?: string
  back?: ScreenNavBack
}

// The shell supplies a back action to an ad-hoc index's selected screen without that screen
// needing to know it was reached that way — every screen renders its own ScreenNav (the prototype
// calls nav() from each screen, never from the shell), so the shell can't just pass a `back` prop
// to a component it doesn't render the top of. An explicit `back` prop on ScreenNav still wins,
// for a screen that wants its own regardless of how it was reached.
const BackContext = createContext<ScreenNavBack | null>(null)

export function BackProvider({ value, children }: { value: ScreenNavBack; children: ReactNode }) {
  return <BackContext.Provider value={value}>{children}</BackContext.Provider>
}

// `{renderQuickAction && renderQuickAction()}` called the registrant's renderer as a plain
// function during ScreenNav's own render — not as a JSX element, so it got no component boundary
// of its own. Any hook the renderer's body calls directly (an open-sheet flag, exactly like
// TasksScreen's sheetOpen, is what #103's quick action needs) would then belong to *ScreenNav's*
// hook list, conditionally, only on renders where something is registered — a Rules of Hooks
// violation waiting to happen. Rendering it as its own always-mounted component instead means the
// renderer's hooks belong to QuickActionSlot's own, separate, consistently-present instance.
function QuickActionSlot() {
  const render = quickAction()
  return render ? <>{render()}</> : null
}

// The React equivalent of variant D's own nav() helper (docs/prototypes/ui-screens.prototype.html
// ~774-786): "the quick-add circle owns the nav's right slot on EVERY screen" — the nav-main title
// row, not the tab bar. #103 fills the circle's behaviour by calling registerQuickAction from its
// own registration file, without editing this component, TabBar.tsx, or App.tsx.
export function ScreenNav({ title, sub, back }: ScreenNavProps) {
  const contextBack = useContext(BackContext)
  const effectiveBack = back ?? contextBack ?? undefined

  return (
    <div className="nav">
      <div className="nav-main">
        {effectiveBack && (
          <button className="icon" onClick={effectiveBack.onBack}>
            ‹ {effectiveBack.label ?? 'Back'}
          </button>
        )}
        <h1>{title}</h1>
        <QuickActionSlot />
      </div>
      {sub && <div className="sub">{sub}</div>}
    </div>
  )
}
