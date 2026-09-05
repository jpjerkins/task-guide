import type { Tab } from './shared/screenRegistry'

// Re-exported so existing `import { type Tab } from './TabBar'` call sites keep working — the
// type itself lives in screenRegistry.ts to avoid an import cycle (screens import registerScreen
// from there without needing TabBar).
export type { Tab }

const TABS: { key: Tab; glyph: string; label: string }[] = [
  { key: 'now', glyph: '◷', label: 'Now' },
  { key: 'tasks', glyph: '☑', label: 'Tasks' },
  { key: 'schedule', glyph: '▤', label: 'Schedule' },
  { key: 'more', glyph: '⋯', label: 'More' },
]

interface TabBarProps {
  active: Tab
  onChange: (tab: Tab) => void
}

// Four equal tabs, nothing else — the prototype's tabbar() (docs/prototypes/ui-screens.prototype.html
// ~788). The accent circle lives in ScreenNav's nav-main right slot, not here.
export function TabBar({ active, onChange }: TabBarProps) {
  return (
    <div className="tabbar">
      {TABS.map((t) => (
        <button
          key={t.key}
          aria-current={t.key === active ? 'page' : undefined}
          onClick={() => onChange(t.key)}
        >
          <span className="g">{t.glyph}</span>
          {t.label}
        </button>
      ))}
    </div>
  )
}
