import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Alert from 'Components/Alert';
import Icon from 'Components/Icon';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import FilterMenu from 'Components/Menu/FilterMenu';
import PageMenuButton from 'Components/Menu/PageMenuButton';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import { align, icons, kinds, sortDirections } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import InteractiveSearchFilterModalConnector from './InteractiveSearchFilterModalConnector';
import InteractiveSearchRow from './InteractiveSearchRow';
import styles from './InteractiveSearch.css';

const columns = [
  {
    name: 'protocol',
    label: () => translate('Source'),
    isSortable: true,
    isVisible: true
  },
  {
    name: 'age',
    label: () => translate('Age'),
    isSortable: true,
    isVisible: true
  },
  {
    name: 'title',
    label: () => translate('Title'),
    isSortable: true,
    isVisible: true
  },
  {
    name: 'indexer',
    label: () => translate('Indexer'),
    isSortable: true,
    isVisible: true
  },
  {
    name: 'size',
    label: () => translate('Size'),
    isSortable: true,
    isVisible: true
  },
  {
    name: 'peers',
    label: () => translate('Peers'),
    isSortable: true,
    isVisible: true
  },
  {
    name: 'qualityWeight',
    label: () => translate('Quality'),
    isSortable: true,
    isVisible: true
  },
  {
    name: 'customFormatScore',
    label: React.createElement(Icon, {
      name: icons.SCORE,
      title: () => translate('CustomFormatScore')
    }),
    isSortable: true,
    isVisible: true
  },
  {
    name: 'indexerFlags',
    label: React.createElement(Icon, {
      name: icons.FLAG,
      title: () => translate('IndexerFlags')
    }),
    isSortable: true,
    isVisible: true
  },
  {
    name: 'rejections',
    label: React.createElement(Icon, {
      name: icons.DANGER
    }),
    isSortable: true,
    fixedSortDirection: sortDirections.ASCENDING,
    isVisible: true
  },
  {
    name: 'releaseWeight',
    label: React.createElement(Icon, { name: icons.DOWNLOAD }),
    isSortable: true,
    fixedSortDirection: sortDirections.ASCENDING,
    isVisible: true
  }
];

class InteractiveSearch extends Component {
  state = { page: 1 };

  componentDidUpdate(previous) {
    if (previous.sortKey !== this.props.sortKey || previous.sortDirection !== this.props.sortDirection || previous.selectedFilterKey !== this.props.selectedFilterKey || previous.type !== this.props.type) {
      this.setState({ page: 1 });
    }
  }

  render() {
    const {
      searchPayload,
      searchProgress,
      isFetching,
      isPopulated,
      error,
      totalReleasesCount,
      items,
      selectedFilterKey,
      filters,
      customFilters,
      sortKey,
      sortDirection,
      type,
      longDateFormat,
      timeFormat,
      onSortPress,
      onFilterSelect,
      onGrabPress
    } = this.props;
    const pageCount = Math.max(1, Math.ceil(items.length / 100));
    const page = Math.min(this.state.page, pageCount);
    const pageItems = items.slice((page - 1) * 100, page * 100);

    return (
      <div>
        <div className={styles.filterMenuContainer}>
          <FilterMenu
            alignMenu={align.RIGHT}
            selectedFilterKey={selectedFilterKey}
            filters={filters}
            customFilters={customFilters}
            buttonComponent={PageMenuButton}
            filterModalConnectorComponent={InteractiveSearchFilterModalConnector}
            filterModalConnectorComponentProps={{ type }}
            onFilterSelect={onFilterSelect}
          />
        </div>

        {
          isFetching ? <div>
            <LoadingIndicator />
            <p role="status">{searchProgress?.phase === 'processing' ? `Checked ${searchProgress.processed} of ${searchProgress.total} releases. Results and ranking are provisional until processing finishes.` : 'Waiting for search providers. Closing this window cancels further processing.'}</p>
          </div> : null
        }

        {
          !isFetching && error ?
            <div className={styles.blankpad}>
              {error.responseJSON?.message || translate('UnableToLoadInteractiveSearch')}
            </div> :
            null
        }

        {
          !isFetching && !error && isPopulated && !totalReleasesCount ?
            <Alert kind={kinds.INFO}>
              {translate('NoResultsFound')}
            </Alert> :
            null
        }

        {
          !!totalReleasesCount && isPopulated && !items.length ?
            <Alert kind={kinds.WARNING}>
              {translate('AllResultsAreHiddenByTheAppliedFilter')}
            </Alert> :
            null
        }

        {
          isPopulated && !!items.length ?
            <Table
              columns={columns}
              sortKey={sortKey}
              sortDirection={sortDirection}
              onSortPress={onSortPress}
            >
              <TableBody>
                {
                  pageItems.map((item) => {
                    return (
                      <InteractiveSearchRow
                        key={`${item.indexerId}-${item.guid}`}
                        {...item}
                        searchPayload={searchPayload}
                        longDateFormat={longDateFormat}
                        timeFormat={timeFormat}
                        onGrabPress={onGrabPress}
                      />
                    );
                  })
                }
              </TableBody>
            </Table> :
            null
        }

        {items.length > 0 ? <div className={styles.filteredMessage}>
          <button disabled={page <= 1} onClick={() => this.setState({ page: page - 1 })}>Previous</button>{' '}
          <span>Page {page} of {pageCount} · {items.length} results in this view</span>{' '}
          <button disabled={page >= pageCount} onClick={() => this.setState({ page: page + 1 })}>Next</button>
        </div> : null}

        {
          totalReleasesCount !== items.length && !!items.length ?
            <div className={styles.filteredMessage}>
              {translate('SomeResultsAreHiddenByTheAppliedFilter')}
            </div> :
            null
        }
      </div>
    );
  }
}

InteractiveSearch.propTypes = {
  searchPayload: PropTypes.object.isRequired,
  searchProgress: PropTypes.object,
  isFetching: PropTypes.bool.isRequired,
  isPopulated: PropTypes.bool.isRequired,
  error: PropTypes.object,
  totalReleasesCount: PropTypes.number.isRequired,
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  selectedFilterKey: PropTypes.oneOfType([PropTypes.string, PropTypes.number]).isRequired,
  filters: PropTypes.arrayOf(PropTypes.object).isRequired,
  customFilters: PropTypes.arrayOf(PropTypes.object).isRequired,
  sortKey: PropTypes.string,
  sortDirection: PropTypes.string,
  type: PropTypes.string.isRequired,
  longDateFormat: PropTypes.string.isRequired,
  timeFormat: PropTypes.string.isRequired,
  onSortPress: PropTypes.func.isRequired,
  onFilterSelect: PropTypes.func.isRequired,
  onGrabPress: PropTypes.func.isRequired
};

export default InteractiveSearch;
