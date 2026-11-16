import AppSectionState, {
  AppSectionDeleteState,
} from 'App/State/AppSectionState';
import Command from 'Commands/Command';

export type CommandAppState = AppSectionState<Command> &
  Partial<AppSectionDeleteState>;

export default CommandAppState;
