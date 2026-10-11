// SENTINEL X custom icon set: 1.5px strokes, asymmetric organic curves.
const S = ({ children, size = 20, ...p }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" {...p}>{children}</svg>
);

export const IconGear = (p) => (
  <S {...p}><path d="M12 3.2c.9 0 1.3.9 1.6 1.7.6.2 1.1.4 1.6.8.9-.3 1.8-.6 2.4 0 .6.6.4 1.6 0 2.4.4.5.6 1 .8 1.6.8.3 1.7.7 1.7 1.6s-.9 1.3-1.7 1.6c-.2.6-.4 1.1-.8 1.6.3.9.6 1.8 0 2.4-.6.6-1.6.4-2.4 0-.5.4-1 .6-1.6.8-.3.8-.7 1.7-1.6 1.7s-1.3-.9-1.6-1.7c-.6-.2-1.1-.4-1.6-.8-.9.3-1.8.6-2.4 0-.6-.6-.4-1.6 0-2.4-.4-.5-.6-1-.8-1.6-.8-.3-1.7-.7-1.7-1.6s.9-1.3 1.7-1.6c.2-.6.4-1.1.8-1.6-.3-.9-.6-1.8 0-2.4.6-.6 1.6-.4 2.4 0 .5-.4 1-.6 1.6-.8.3-.8.7-1.7 1.6-1.7z" /><path d="M14.6 12.4a2.7 2.7 0 1 1-2.9-3.1" /></S>
);
export const IconBase = (p) => (
  <S {...p}><path d="M4 15.5c2.2-1.6 5-2.4 8-2.4s5.8.8 8 2.4" /><path d="M6.5 12c1.6-1 3.5-1.6 5.5-1.6 2 0 3.9.6 5.5 1.6" /><path d="M9.2 8.6c.9-.4 1.8-.6 2.8-.6s1.9.2 2.8.6" /><path d="M12 15.6v4.6" /><circle cx="12" cy="20.4" r=".6" fill="currentColor" /></S>
);
export const IconDevices = (p) => (
  <S {...p}><rect x="3.5" y="5" width="11" height="8.5" rx="2.6" /><path d="M7 17.5h4.5M9.2 13.5v4" /><path d="M17.8 8.2c1.6.2 2.7 1 2.7 2.4v5.8c0 1.3-1 2.1-2.4 2.1s-2.4-.8-2.4-2.1v-3.2" /><circle cx="18.1" cy="15.9" r=".5" fill="currentColor" /></S>
);
export const IconTasks = (p) => (
  <S {...p}><path d="M4.5 7.2h9.5M4.5 12h6.5M4.5 16.8h8" /><path d="M16.5 14.2l1.8 1.9 3.2-4" /><circle cx="18.5" cy="7.2" r="1.6" /></S>
);
export const IconSend = (p) => (
  <S {...p}><path d="M4.8 12.2c4.6-1.9 9.4-4.4 14.7-7.2-1.6 5.6-3.8 10.4-6.4 14.4-.9-2.5-1.9-4.6-3.4-6.1-1.4-.6-3-1-4.9-1.1z" /><path d="M9.7 13.3l4.3-3.6" /></S>
);
export const IconClose = (p) => (<S {...p}><path d="M6.5 6.8c3.6 3.1 7.4 7 11 10.6M17.3 6.6c-3.4 3.4-7.1 7.2-10.6 10.8" /></S>);
export const IconPlus = (p) => (<S {...p}><path d="M12 5.5v13M5.5 12h13" /></S>);
export const IconTrash = (p) => (<S {...p}><path d="M5 7h14M9.5 7V5.3h5V7M7 7l.8 11.2c.1.9.8 1.5 1.7 1.5h5c.9 0 1.6-.6 1.7-1.5L17 7" /></S>);
export const IconPower = (p) => (<S {...p}><path d="M12 4v7" /><path d="M7.4 7.2a6.5 6.5 0 1 0 9.2 0" /></S>);
export const IconStop = (p) => (<S {...p}><rect x="7" y="7" width="10" height="10" rx="2.4" /></S>);
export const IconChevron = (p) => (<S {...p}><path d="M9 6.5l5.5 5.5L9 17.5" /></S>);
export const IconMicDot = (p) => (<S {...p}><path d="M12 4.5a2.6 2.6 0 0 1 2.6 2.6v4.4a2.6 2.6 0 0 1-5.2 0V7.1A2.6 2.6 0 0 1 12 4.5z" /><path d="M7 11.5a5 5 0 0 0 10 0M12 16.5v3" /></S>);
export const IconCopy = (p) => (<S {...p}><rect x="8.5" y="8.5" width="11" height="11" rx="2.6" /><path d="M15.5 8.5V6.6c0-1.2-.9-2.1-2.1-2.1H6.6c-1.2 0-2.1.9-2.1 2.1v6.8c0 1.2.9 2.1 2.1 2.1h1.9" /></S>);
