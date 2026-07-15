/**
 * The design system.
 *
 * Screens import from here and nowhere else. A screen that reaches past this barrel for a raw
 * Tailwind colour, or hand-rolls its own button, is the beginning of the legacy app's problem: 44
 * controllers, each with its own grid, its own validation and its own idea of what a button is.
 *
 * The rule: if two screens need it, it belongs here.
 */
export { Button, type ButtonProps } from "./button";
export { Input, PasswordInput, Select, Textarea, Checkbox, type InputProps } from "./input";
export { Card, CardHeader, Badge, Skeleton } from "./card";
export { Dialog } from "./dialog";
export { ErrorBanner } from "./error-banner";
export { toast, Toaster } from "./toast";
export { Spinner, LoadingPanel, ProgressBar, Stagger, FadeIn } from "./motion";
export { AnimatedNumber } from "./animated-number";
export { RouteProgress } from "./route-progress";
