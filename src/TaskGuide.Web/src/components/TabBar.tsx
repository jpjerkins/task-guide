import { quickAction, type Tab } from './shared/screenRegistry'

// Re-exported so existing `import { type Tab } from './TabBar'` call sites keep working — the
// type itself lives in screenRegistry.ts to avoid an import cycle (TabBar reads the registry's
// quick-action slot; the registry must not import TabBar).
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

export function TabBar({ active, onChange }: TabBarProps) {
  const renderQuickAction = quickAction()

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
      {/* The accent circle's slot, present on every screen (#111); #103 fills it by calling
          registerQuickAction from its own registration file, without editing this component.
          Holds its width even when empty so the bar doesn't reflow once something registers. */}
      <div className="tabbar-quick-action">{renderQuickAction && renderQuickAction()}</div>
    </div>
  )
}
