import '@testing-library/jest-dom/vitest'

// jsdom implements no layout, so scrollIntoView is absent from every Element; DateRail (#140)
// calls it on every render. Stubbed once here rather than in each test file that renders it.
if (!Element.prototype.scrollIntoView) Element.prototype.scrollIntoView = () => {}
