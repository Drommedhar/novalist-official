# Store custom reference lists by entry ID

Custom Codex reference lists store an ordered JSON array of distinct entry IDs inside the existing string-valued field map. The field definition specifies one target entity type. Empty lists use `[]`; existing single-reference fields retain their current format.

Names can contain commas, collide, and change. IDs preserve the selected entries in those cases. Keeping the field map string-valued avoids changing the project format and existing extension field contracts to accept heterogeneous values. The cost is an explicit encoding step for lists; the import schema documents that encoding and supplies an empty-list example.

Deleted targets remain in stored lists until the writer removes them. Entry editors identify them as unavailable, while Wiki and focus previews link only targets that still exist and match the declared type.
