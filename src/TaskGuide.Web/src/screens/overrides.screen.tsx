import { OverrideScreen } from '../components/OverrideScreen'
import { registerScreen } from '../components/shared/screenRegistry'

// Follow tasks.screen.tsx: this module accepts its own HMR update and unregisters on disposal.
if (import.meta.hot) {
  import.meta.hot.accept()
}

registerScreen({
  id: 'overrides',
  tab: 'schedule',
  title: 'Override a date',
  render: () => <OverrideScreen />,
}, import.meta.hot)
