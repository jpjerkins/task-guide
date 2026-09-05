import { TasksScreen } from '../components/TasksScreen'
import { registerScreen } from '../components/shared/screenRegistry'

// Convention for every *.screen.tsx — copy these three lines and the `import.meta.hot` argument
// below. Vite only recognises a direct accept call written in the module itself, so this file
// becomes its own HMR boundary; `registerScreen`'s paired dispose then unregisters just this id
// before the module re-runs, leaving sibling screens registered (#119). Omitting them is not
// silent breakage — the update propagates to App.tsx, which invalidates into a full page reload —
// you just lose the hot swap.
if (import.meta.hot) {
  import.meta.hot.accept()
}

registerScreen({
  id: 'tasks',
  tab: 'tasks',
  title: 'Tasks',
  render: () => <TasksScreen />,
}, import.meta.hot)
