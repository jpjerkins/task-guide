import { useState } from 'react'
import { PlaceholderScreen } from './components/PlaceholderScreen'
import { TabBar, type Tab } from './components/TabBar'
import { installHmrGuard, resetRegistry, screensFor } from './components/shared/screenRegistry'
import { BackProvider, ScreenNav } from './components/shared/ScreenNav'

// Each file under ./screens registers itself as a module side effect (registerScreen). This is
// the ONLY place that needs to know the directory exists — a new Web ticket adds its own
// `<name>.screen.tsx` file there and this glob picks it up with zero edits here.
import.meta.glob('./screens/**/*.screen.tsx', { eager: true })

// A screen file exports nothing, so it isn't its own Fast Refresh boundary — an edit to one
// propagates up to here (the nearest module that accepts the update, via the glob above), which
// then re-executes the glob against a registry that was never cleared, and registerScreen's
// duplicate-id guard throws on every dev edit. Clearing the registry right before Vite re-runs
// this module keeps that guard's contract — a real duplicate is still a defect in production —
// without punishing every hot reload for it.
installHmrGuard(import.meta.hot, resetRegistry)

const TAB_TITLES: Record<Tab, string> = {
  now: 'Now',
  tasks: 'Tasks',
  schedule: 'Schedule',
  more: 'More',
}

export default function App() {
  const [tab, setTab] = useState<Tab>('tasks')
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const screens = screensFor(tab)

  function changeTab(next: Tab) {
    setTab(next)
    setSelectedId(null)
  }

  let content
  if (screens.length === 0) {
    content = <PlaceholderScreen title={TAB_TITLES[tab]} />
  } else if (screens.length === 1) {
    content = screens[0].render()
  } else {
    const selected = screens.find((s) => s.id === selectedId)
    if (selected) {
      // Every screen renders its own ScreenNav (the prototype's nav() is called by each screen,
      // never by the shell — docs/prototypes/ui-screens.prototype.html), so the shell must not
      // wrap it in a second one. The back action still needs to reach that screen's ScreenNav
      // without it knowing it was reached via an index, so it travels through context instead.
      content = <BackProvider value={{ onBack: () => setSelectedId(null) }}>{selected.render()}</BackProvider>
    } else {
      content = (
        <>
          <ScreenNav title={TAB_TITLES[tab]} />
          <div className="scroll">
            <div className="list">
              {screens.map((s) => (
                <button key={s.id} className="row" onClick={() => setSelectedId(s.id)}>
                  <span className="body title">{s.title}</span>
                </button>
              ))}
            </div>
          </div>
        </>
      )
    }
  }

  return (
    <div className="device">
      {content}
      <TabBar active={tab} onChange={changeTab} />
    </div>
  )
}
