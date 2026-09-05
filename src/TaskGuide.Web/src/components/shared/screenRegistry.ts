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

// Seven Web tickets across two concurrently-running lanes each register a screen. A duplicate
// `id` is a defect, not a merge — mirrors the Dimension registry's rule that a value name belongs
// to exactly one Dimension: rejected before the service will run, not resolved at the point of use.
export function registerScreen(screen: ScreenDescriptor): void {
  if (screens.some((s) => s.id === screen.id)) {
    throw new Error(`registerScreen: duplicate screen id "${screen.id}"`)
  }
  screens.push(screen)
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
