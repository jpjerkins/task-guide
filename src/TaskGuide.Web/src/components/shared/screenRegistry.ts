import type { ReactNode } from 'react'

// The tab identifiers the shell renders. Owned here (not TabBar.tsx) so the registry has no
// import-cycle back to the component that consumes it; TabBar re-exports this type so existing
// `import { type Tab } from './TabBar'` call sites keep working unchanged.
export type Tab = 'now' | 'tasks' | 'schedule' | 'more'

export interface ScreenDescriptor {
  id: string
  tab: Tab
  title: string
  order?: number
  render: () => ReactNode
}

const DEFAULT_ORDER = 100

let screens: ScreenDescriptor[] = []
let quickActionRenderer: (() => ReactNode) | null = null

interface ScreenModuleHot {
  dispose: (cb: () => void) => void
}

// Seven Web tickets across two concurrently-running lanes each register a screen. A duplicate
// `id` is a defect, not a merge — mirrors the Dimension registry's rule that a value name belongs
// to exactly one Dimension: rejected before the service will run, not resolved at the point of use.
export function registerScreen(screen: ScreenDescriptor, hot?: ScreenModuleHot): void {
  if (screens.some((s) => s.id === screen.id)) {
    throw new Error(`registerScreen: duplicate screen id "${screen.id}"`)
  }
  screens.push(screen)

  // The screen module itself opts into handling its update, so Vite does not propagate it to
  // App.tsx. Its dispose callback removes only this registration; sibling screens stay registered
  // while this module re-executes and registers its replacement.
  if (hot) {
    hot.dispose(() => {
      screens = screens.filter((registered) => registered.id !== screen.id)
    })
  }
}

export function screensFor(tab: Tab): ScreenDescriptor[] {
  return screens
    .filter((s) => s.tab === tab)
    .slice()
    .sort((a, b) => {
      const orderA = a.order ?? DEFAULT_ORDER
      const orderB = b.order ?? DEFAULT_ORDER
      return orderA !== orderB ? orderA - orderB : a.id.localeCompare(b.id)
    })
}

// The accent circle owns the nav's right slot on every screen (#111); #103 fills its behaviour by
// calling registerQuickAction from its own registration file, without editing ScreenNav.tsx or
// App.tsx. A second registration is the same hazard registerScreen's duplicate-id guard exists
// for — two lanes both claiming the slot would otherwise silently lose one — so it throws too.
export function registerQuickAction(render: () => ReactNode): void {
  if (quickActionRenderer !== null) {
    throw new Error('registerQuickAction: a quick action is already registered')
  }
  quickActionRenderer = render
}

export function quickAction(): (() => ReactNode) | null {
  return quickActionRenderer
}

// Test-only seam so registrations from one test don't leak into the next.
export function resetRegistry(): void {
  screens = []
  quickActionRenderer = null
}

// App.tsx is a Fast Refresh boundary, but its eager imports are cached on an App-only update. A
// partial update would therefore leave the registry without re-running every screen module. Turn
// that one case into a full reload, which rebuilds the registry from a clean module graph.
export function installHmrGuard(
  hot: { dispose: (cb: () => void) => void; invalidate: () => void } | undefined,
): void {
  if (hot) {
    hot.dispose(() => hot.invalidate())
  }
}
