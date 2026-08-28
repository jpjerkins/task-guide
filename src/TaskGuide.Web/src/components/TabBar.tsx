export type Tab = 'now' | 'tasks' | 'schedule' | 'more'

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
