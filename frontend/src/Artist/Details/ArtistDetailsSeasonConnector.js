import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import * as commandNames from 'Commands/commandNames';
import { setAlbumsSort, setAlbumsTableOption, toggleAlbumsMonitored } from 'Store/Actions/albumActions';
import { executeCommandHelper } from 'Store/Actions/commandActions';
import createArtistSelector from 'Store/Selectors/createArtistSelector';
import createClientSideCollectionSelector from 'Store/Selectors/createClientSideCollectionSelector';
import createCommandsSelector from 'Store/Selectors/createCommandsSelector';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import createUISettingsSelector from 'Store/Selectors/createUISettingsSelector';
import { isCommandExecuting } from 'Utilities/Command';
import ArtistDetailsSeason from './ArtistDetailsSeason';

export function createMapStateToProps() {
  return createSelector(
    (state, { label }) => label,
    createClientSideCollectionSelector('albums'),
    createArtistSelector(),
    createCommandsSelector(),
    createDimensionsSelector(),
    createUISettingsSelector(),
    (label, albums, artist, commands, dimensions, uiSettings) => {
      const albumsByType = albums.items.filter(({ artistId, albumType }) => artistId === artist.id && albumType === label);
      const searchAlbumIds = [...new Set(albumsByType.filter(({ monitored }) => monitored).map(({ id }) => id))];
      const isSearching = commands.some((command) => {
        if (!isCommandExecuting(command)) {
          return false;
        }

        return (command.name === commandNames.ARTIST_SEARCH && command.body?.artistId === artist.id) ||
          (command.name === commandNames.ALBUM_SEARCH && command.body?.albumIds?.some((id) => searchAlbumIds.includes(id)));
      });

      const isSearchingUnsatisfied = commands.some((command) =>
        command.name === commandNames.ARTIST_UNSATISFIED_SEARCH &&
        command.body?.artistId === artist.id &&
        command.body.albumType === label &&
        isCommandExecuting(command)
      );

      return {
        items: albumsByType,
        searchAlbumIds,
        isSearching,
        isSearchingUnsatisfied,
        columns: albums.columns,
        artistMonitored: artist.monitored,
        sortKey: albums.sortKey,
        sortDirection: albums.sortDirection,
        isSmallScreen: dimensions.isSmallScreen,
        uiSettings
      };
    }
  );
}

function mapDispatchToProps(dispatch) {
  return {
    toggleAlbumsMonitored: (payload) => dispatch(toggleAlbumsMonitored(payload)),
    setAlbumsTableOption: (payload) => dispatch(setAlbumsTableOption(payload)),
    dispatchSetAlbumSort: (payload) => dispatch(setAlbumsSort(payload)),
    queueSearch: (payload) => executeCommandHelper(payload, dispatch)
  };
}

export class ArtistDetailsSeasonConnector extends Component {

  constructor(props, context) {
    super(props, context);
    this.state = { queueingSearchName: null, searchError: false };
    this._queueingSearchName = null;
    this._requestId = 0;
    this._isMounted = false;
  }

  componentDidMount() {
    this._isMounted = true;
  }

  componentDidUpdate(prevProps) {
    if (prevProps.artistId !== this.props.artistId) {
      this._requestId++;
      this._queueingSearchName = null;
      this.setState({ queueingSearchName: null, searchError: false });
    }
  }

  componentWillUnmount() {
    this._isMounted = false;
  }

  //
  // Listeners

  onTableOptionChange = (payload) => {
    this.props.setAlbumsTableOption(payload);
  };

  onSortPress = (sortKey) => {
    this.props.dispatchSetAlbumSort({ sortKey });
  };

  onMonitorAlbumsPress = (albumIds, monitored) => {
    this.props.toggleAlbumsMonitored({
      albumIds,
      monitored
    });
  };

  onSearchPress = () => {
    if (this.props.isSearching || !this.props.searchAlbumIds.length) {
      return;
    }

    return this.queueSearch({
      name: commandNames.ALBUM_SEARCH,
      albumIds: this.props.searchAlbumIds
    });
  };

  onSearchUnsatisfiedPress = () => {
    const { artistId, artistMonitored, label, searchAlbumIds, isSearchingUnsatisfied } = this.props;
    if (isSearchingUnsatisfied || !artistMonitored || !searchAlbumIds.length) {
      return;
    }

    return this.queueSearch({
      name: commandNames.ARTIST_UNSATISFIED_SEARCH,
      artistId,
      albumType: label
    });
  };

  queueSearch = async(payload) => {
    if (this._queueingSearchName) {
      return;
    }

    this._queueingSearchName = payload.name;
    const requestId = ++this._requestId;
    this.setState({ queueingSearchName: payload.name, searchError: false });
    try {
      await this.props.queueSearch(payload);
    } catch {
      if (this._isMounted && requestId === this._requestId) {
        this.setState({ searchError: true });
      }
    } finally {
      if (requestId === this._requestId) {
        this._queueingSearchName = null;
        if (this._isMounted) {
          this.setState({ queueingSearchName: null });
        }
      }
    }
  };

  //
  // Render

  render() {
    const { queueingSearchName, searchError } = this.state;

    return (
      <ArtistDetailsSeason
        {...this.props}
        isSearching={queueingSearchName === commandNames.ALBUM_SEARCH || this.props.isSearching}
        isSearchingUnsatisfied={queueingSearchName === commandNames.ARTIST_UNSATISFIED_SEARCH || this.props.isSearchingUnsatisfied}
        isQueueingSearch={!!queueingSearchName}
        searchError={searchError}
        onSearchPress={this.onSearchPress}
        onSearchUnsatisfiedPress={this.onSearchUnsatisfiedPress}
        onSortPress={this.onSortPress}
        onTableOptionChange={this.onTableOptionChange}
        onMonitorAlbumsPress={this.onMonitorAlbumsPress}
      />
    );
  }
}

ArtistDetailsSeasonConnector.propTypes = {
  artistId: PropTypes.number.isRequired,
  toggleAlbumsMonitored: PropTypes.func.isRequired,
  setAlbumsTableOption: PropTypes.func.isRequired,
  dispatchSetAlbumSort: PropTypes.func.isRequired,
  artistMonitored: PropTypes.bool.isRequired,
  label: PropTypes.string.isRequired,
  queueSearch: PropTypes.func.isRequired,
  searchAlbumIds: PropTypes.arrayOf(PropTypes.number).isRequired,
  isSearching: PropTypes.bool.isRequired,
  isSearchingUnsatisfied: PropTypes.bool.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(ArtistDetailsSeasonConnector);
