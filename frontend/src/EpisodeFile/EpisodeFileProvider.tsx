import React, {
  createContext,
  PropsWithChildren,
  useContext,
  useMemo,
} from 'react';
import findById from 'Utilities/Array/findById';
import { EpisodeFile } from './EpisodeFile';
import useEpisodeFiles, { EpisodeFileFilter } from './useEpisodeFiles';

export const EpisodeFileContext = createContext<
  ReadonlyArray<EpisodeFile> | undefined
>(undefined);

export default function EpisodeFileProvider({
  children,
  ...filter
}: PropsWithChildren<EpisodeFileFilter>) {
  const { data } = useEpisodeFiles(filter);

  return (
    <EpisodeFileContext.Provider value={data}>
      {children}
    </EpisodeFileContext.Provider>
  );
}

function useEpisodeFileFromContext(id: number | undefined) {
  const episodeFiles = useContext(EpisodeFileContext);

  return useMemo(() => findById(episodeFiles, id), [id, episodeFiles]);
}

export function useEpisodeFile(id: number | undefined) {
  const hasEpisodeFileContext = !!useContext(EpisodeFileContext);
  const contextEpisodeFile = useEpisodeFileFromContext(id);

  const { data } = useEpisodeFiles({
    episodeFileIds: id && !hasEpisodeFileContext ? [id] : [],
  });

  return contextEpisodeFile ?? data.find((item) => item.id === id);
}

export interface SeriesEpisodeFile {
  count: number;
  episodesWithFiles: number;
}
