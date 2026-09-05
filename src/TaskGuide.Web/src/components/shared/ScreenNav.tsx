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

// The React equivalent of variant D's own nav() helper (docs/prototypes/ui-screens.prototype.html
// ~774-786): "the quick-add circle owns the nav's right slot on EVERY screen" — the nav-main title
// row, not the tab bar. #103 fills the circle's behaviour by calling registerQuickAction from its
// own registration file, without editing this component, TabBar.tsx, or App.tsx.
export function ScreenNav({ title, sub, back }: ScreenNavProps) {
  const renderQuickAction = quickAction()

  return (
    <div className="nav">
      <div className="nav-main">
        {back && (
          <button className="icon" onClick={back.onBack}>
            ‹ {back.label ?? 'Back'}
          </button>
        )}
        <h1>{title}</h1>
        {renderQuickAction && renderQuickAction()}
      </div>
      {sub && <div className="sub">{sub}</div>}
    </div>
  )
}
