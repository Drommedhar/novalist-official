/** Wire shape of project/getSceneEdit and project/setSceneDateRange
 * (Novalist.Backend SceneEditDto). Shared by the scene and story-date dialogs. */
export interface SceneEditDto {
  pov: string
  dateStart: string
  dateEnd: string
  dateNote: string
}
