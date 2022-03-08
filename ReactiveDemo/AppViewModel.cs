using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using ReactiveUI;

namespace ReactiveDemo
{
    // AppViewModel is where we will describe the interaction of our application.
    // We can describe the entire application in one class since it's very small now.
    // Most ViewModels will derive off ReactiveObject, while most Model classes will
    // most derive off INotifyPropertyChanged

    // AppViewModel은 우리 애플리케이션의 상호작용을 기술할 곳이다.
    // 우리는 현재 애플리케이션이 매우 작기 때문에 클래스 하나에 전체 앱을 묘사할 수 있다.
    // 대부분의 ViewModel은 ReactiveObject에서 파생된다. 반면, 대부분의 Model은 INotifyPropertyChanged에서 파생될 것이다.

    public class AppViewModel : ReactiveObject
    {
        // In ReactiveUI, this is the syntax to declare a read-write property
        // that will notify Observers, as well as WPF, that a property has
        // changed. If we declared this as a normal property, we couldn't tell
        // when it has changed!

        // ReactiveUI에서 이 부분은 프로퍼티가 변경되었다는 것을 WPF뿐만 아니라 Observers에게 알리는 read-write 프로퍼티를 선언하는 문법이다.
        // 만약에 우리가 이 부분을 normal property로 선언한다면, 우리는 그것이 변경되었을 때, 알리지 못 할 것이다.
        private string _searchTerm;

        public string SearchTerm
        {
            get => _searchTerm;
            set => this.RaiseAndSetIfChanged(ref _searchTerm, value);
        }

        // Here's the interesting part: In ReactiveUI, we can take IObservables
        // and "pipe" them to a Property - whenever the Observable yields a new
        // value, we will notify ReactiveObject that the property has changed.
        //
        // 여기 흥미로운 부분이 있다. ReactiveUI에서 우리는 IObservables를 가져올 수 있고, 그것들에게 Property를 보낼 수 있다.
        // Observables가 새로운 값을 낼 때마다, 우리는 ReactiveObject에게 프로퍼티가 바뀌었다고 알릴 것이다.

        // To do this, we have a class called ObservableAsPropertyHelper - this
        // class subscribes to an Observable and stores a copy of the latest value.
        // It also runs an action whenever the property changes, usually calling
        // ReactiveObject's RaisePropertyChanged.
        //
        // 이것을 하기 위해, 우리는 ObservableAsPropertyHelper라고 불리는 클래스를 하나 가진다.
        // 이 클래스는 Observable하나를 구독하고 가장 최신의 값의 복사본을 저장한다.
        // 그것은 프로퍼티가 변화될 때마다 action을 실행하는 데, 보통 ReactiveObject's RaisePropertyChanged라고 부른다.

        private readonly ObservableAsPropertyHelper<IEnumerable<NugetDetailsViewModel>> _searchResults;

        public IEnumerable<NugetDetailsViewModel> SearchResults => _searchResults.Value;

        // Here, we want to create a property to represent when the application
        // is performing a search (i.e. when to show the "spinner" control that
        // lets the user know that the app is busy). We also declare this property
        // to be the result of an Observable (i.e. its value is derived from
        // some other property)

        // 여기서 우리는 앱이 검색을 수행할 때
        // (즉, 사용자가 앱이 사용중임을 알 수 있는 spinner 컨트롤을 표시할 때)
        // 나타낼 속성을 만들려고 한다.
        // 우리는 또한 이 프로퍼티가 Observable의 결과라는 것을 선언한다.
        // (즉, 그것의 값이 다른 프로퍼티로부터 파생되었다.)
        private readonly ObservableAsPropertyHelper<bool> _isAvailable;

        public bool IsAvailable => _isAvailable.Value;

        public AppViewModel()
        {
            // Creating our UI declaratively
            // 선언적으로 우리 UI 생성하기
            //
            // The Properties in this ViewModel are related to each other in different
            // ways - with other frameworks, it is difficult to describe each relation
            // succinctly; the code to implement "The UI spinner spins while the search
            // is live" usually ends up spread out over several event handlers.
            // 
            // However, with ReactiveUI, we can describe how properties are related in a
            // very organized clear way. Let's describe the workflow of what the user does
            // in this application, in the order they do it.

            // We're going to take a Property and turn it into an Observable here - this
            // Observable will yield a value every time the Search term changes, which in
            // the XAML, is connected to the TextBox.
            //
            // We're going to use the Throttle operator to ignore changes that happen too
            // quickly, since we don't want to issue a search for each key pressed! We
            // then pull the Value of the change, then filter out changes that are identical,
            // as well as strings that are empty.
            //
            // We then do a SelectMany() which starts the task by converting Task<IEnumerable<T>>
            // into IObservable<IEnumerable<T>>. If subsequent requests are made, the
            // CancellationToken is called. We then ObservableOn the main thread,
            // everything up until this point has been running on a separate thread due
            // to the Throttle().
            //
            // We then use a ObservableAsPropertyHelper and the ToProperty() method to allow
            // us to have the latest results that we can expose through the property to the View.
            _searchResults = this
                .WhenAnyValue(x => x.SearchTerm)
                .Throttle(TimeSpan.FromMilliseconds(800))
                .Select(term => term?.Trim())
                .DistinctUntilChanged()
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .SelectMany(SearchNuGetPackages)
                .ObserveOn(RxApp.MainThreadScheduler)
                .ToProperty(this, x => x.SearchResults);

            // We subscribe to the "ThrownExceptions" property of our OAPH, where ReactiveUI
            // marshals any exceptions that are thrown in SearchNuGetPackages method.
            // See the "Error Handling" section for more information about this.
            _searchResults.ThrownExceptions.Subscribe(error => { /* Handle errors here */ });

            // A helper method we can use for Visibility or Spinners to show if results are available.
            // We get the latest value of the SearchResults and make sure it's not null.
            _isAvailable = this
                .WhenAnyValue(x => x.SearchResults)
                .Select(searchResults => searchResults != null)
                .ToProperty(this, x => x.IsAvailable);
        }

        // Here we search NuGet packages using the NuGet.Client library. Ideally, we should
        // extract such code into a separate service, say, INuGetSearchService, but let's
        // try to avoid overcomplicating things at this time.
        private async Task<IEnumerable<NugetDetailsViewModel>> SearchNuGetPackages(
            string term, CancellationToken token)
        {
            var providers = new List<Lazy<INuGetResourceProvider>>();
            providers.AddRange(Repository.Provider.GetCoreV3()); // Add v3 API support
            var package = new PackageSource("https://api.nuget.org/v3/index.json");
            var source = new SourceRepository(package, providers);

            var filter = new SearchFilter(false);
            var resource = await source.GetResourceAsync<PackageSearchResource>().ConfigureAwait(false);
            var metadata = await resource.SearchAsync(term, filter, 0, 10, new NuGet.Common.NullLogger(), token).ConfigureAwait(false);
            return metadata.Select(x => new NugetDetailsViewModel(x));
        }
    }
}
